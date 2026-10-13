using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace DiseaseMutationsApp.E2E;

/// <summary>The Diana page ("/"): inputs, runs, results, limits and permalinks, against the fake pipeline.</summary>
public class DesignPageTests : E2EBase
{
    [Test]
    public async Task PageHasATitle_AndOneH1()
    {
        await GotoAsync("/");
        await Expect(Page).ToHaveTitleAsync("Diana");
        await Expect(Page.Locator("main h1")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task SingleVariant_RunsAndShowsBothResultTables()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T");

        await Expect(DoneStatus).ToContainTextAsync("1 of 1 variants analysed");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "NC_000017.11:g.7674220C>T" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.GetByRole(AriaRole.Table)).ToHaveCountAsync(2);
        await Expect(Page.Locator("table").First.Locator("tbody tr")).ToHaveCountAsync(5);
    }

    [Test]
    public async Task ResultTable_SortsByColumn_AndExposesTheSortState()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T");
        await Expect(DoneStatus).ToBeVisibleAsync();

        var table = Page.GetByRole(AriaRole.Table).First;
        var energyHeader = table.GetByRole(AriaRole.Columnheader, new() { NameRegex = new(@"Energy") });
        await energyHeader.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(energyHeader).ToHaveAttributeAsync("aria-sort", new System.Text.RegularExpressions.Regex("ascending|descending"));
    }

    [Test]
    public async Task InvalidVariant_ShowsTheReason_AndOffersRetry_WithoutStoppingOthers()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T, FAIL_VARIANT");

        await Expect(DoneStatus).ToContainTextAsync("1 failed");
        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("FAIL_VARIANT") }).ClickAsync();
        await Expect(Page.GetByText("Reference mismatch at NC_1.1")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry this variant" })).ToBeVisibleAsync();
        // The good variant is untouched.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "NC_000017.11:g.7674220C>T" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Table)).ToHaveCountAsync(2);
    }

    [Test]
    public async Task RsId_ResolvesToVariants_OnBothStrands()
    {
        await GotoAsync("/");
        await RunAsync("rs334");

        await Expect(DoneStatus).ToContainTextAsync("4 of 4 variants analysed");
        var variants = Page.GetByRole(AriaRole.Tablist, new() { Name = "Variants of rs334" });
        await Expect(variants.GetByRole(AriaRole.Tab)).ToHaveCountAsync(4);   // two notations, each normal + complement
    }

    [Test]
    public async Task CancelRun_StopsASlowVariant()
    {
        await GotoAsync("/");
        await RunAsync("SLOW_VARIANT");
        await Expect(CancelRunButton).ToBeVisibleAsync();
        await CancelRunButton.ClickAsync();

        await Expect(RunButton).ToBeEnabledAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("SLOW_VARIANT") })).ToContainTextAsync("cancelled", new() { IgnoreCase = true });
    }

    [Test]
    public async Task Permalink_FillsTheForm_ButNeverStartsARun()
    {
        await GotoAsync("/?hgvs=NC_000017.11:g.7674220C%3ET&spacer=30&seed=10-17");

        await Expect(VariantsBox).ToHaveValueAsync("NC_000017.11:g.7674220C>T");
        await Expect(Page.GetByLabel("Spacer size")).ToHaveValueAsync("30");
        await Expect(Page.GetByText("opened from a link")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(0);          // no run started by opening the link

        await RunButton.ClickAsync();
        await Expect(DoneStatus).ToContainTextAsync("1 of 1 variants analysed");
    }

    [TestCase("17")]
    [TestCase("61")]
    public async Task SpacerOutsideRange_ShowsAMessage_AndBlocksTheRun(string size)
    {
        await GotoAsync("/");
        await VariantsBox.FillAsync("NC_000017.11:g.7674220C>T");
        await Page.GetByLabel("Spacer size").FillAsync(size);
        await Page.GetByLabel("Spacer size").PressAsync("Tab");   // @bind commits on change (blur)

        await Expect(Page.GetByText("Spacer size must be between 18 and 60")).ToBeVisibleAsync();
        await Expect(RunButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task MoreThan50Inputs_IsRefused_WithTheCount()
    {
        await GotoAsync("/");
        var many = string.Join(", ", Enumerable.Range(1, 51).Select(i => $"NC_000017.11:g.{7674000 + i}C>T"));
        await VariantsBox.FillAsync(many);

        await Expect(Page.GetByText("At most 50 variants per run; you entered 51")).ToBeVisibleAsync();
        await Expect(RunButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task CsvReport_Downloads_WithABom_AndTheHeader()
    {
        await GotoAsync("/");
        await RunAsync("rs334");   // the report download is offered for rsID results
        await Expect(DoneStatus).ToContainTextAsync("4 of 4");

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "Download all reports" }).ClickAsync());
        var path = await download.PathAsync();
        var bytes = await File.ReadAllBytesAsync(path!);

        Assert.Multiple(() =>
        {
            Assert.That(download.SuggestedFilename, Does.EndWith(".csv"));
            Assert.That(bytes.Take(3).ToArray(), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 byte-order mark for Excel");
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.That(text, Does.Contain("RS ID,HGVS,Sequence Type,Rank,Sequence,Score,GC Content,Alignments,Seed Region,Homopolymers,Fold Energy,Strand"));
            Assert.That(text, Does.Contain("Mutated").And.Contain("Original"));
        });
    }

    [Test]
    public async Task ThemeToggle_ChangesTheTheme_AndSurvivesAReload()
    {
        await GotoAsync("/");
        var before = await Page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme')");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("Switch to (light|dark) theme") }).ClickAsync();
        // The toggle is a server round trip (Blazor event, then JS interop), so wait for the attribute instead of sampling it at once.
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync("data-bs-theme", before);
        var after = await Page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme')");

        await Page.ReloadAsync();
        await Expect(Page.Locator("h1")).ToBeVisibleAsync();
        Assert.That(await Page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme')"), Is.EqualTo(after));
    }

    [Test]
    public async Task TabStrip_MovesWithTheArrowKeys()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T, NC_000017.11:g.7674221C>T");
        await Expect(DoneStatus).ToContainTextAsync("2 of 2");

        var first = Page.GetByRole(AriaRole.Tab).First;
        await first.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.GetByRole(AriaRole.Tab).Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
    }
}

/// <summary>The CSV report is available for every input, not only for rsIDs.</summary>
public class ReportDownloadTests : E2EBase
{
    private static async Task<string> TextOf(IDownload download) =>
        System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync((await download.PathAsync())!));

    [Test]
    public async Task PlainHgvs_HasAPerTabReport_AndAllInputsReport()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T, NC_000017.11:g.7674221C>T");
        await Expect(DoneStatus).ToContainTextAsync("2 of 2 variants analysed");

        var one = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "Download report" }).ClickAsync());
        var oneText = await TextOf(one);

        var all = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "Download all reports" }).ClickAsync());
        var allText = await TextOf(all);

        Assert.Multiple(() =>
        {
            Assert.That(one.SuggestedFilename, Does.StartWith("Report_NC_000017.11_g.7674220C_T_").And.EndWith(".csv"));
            Assert.That(oneText, Does.Contain(",NC_000017.11:g.7674220C>T,Mutated,").And.Not.Contain("g.7674221"));
            Assert.That(oneText, Does.Contain(",NC_000017.11:g.7674220C>T,Original,"));
            Assert.That(all.SuggestedFilename, Does.StartWith("Report_All_"));
            Assert.That(allText, Does.Contain("g.7674220C>T").And.Contain("g.7674221C>T"));
            // The first column (RS ID) is empty for a direct HGVS input, as in the CLI report.
            Assert.That(allText.Split('\n').First(l => l.Contains("Mutated")), Does.StartWith(",NC_000017.11"));
        });
    }

    [Test]
    public async Task ReportIsNotOfferedWhileNothingHasResults()
    {
        await GotoAsync("/");
        await RunAsync("FAIL_VARIANT");
        await Expect(DoneStatus).ToContainTextAsync("1 failed");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Download all reports" })).ToHaveCountAsync(0);
    }
}

/// <summary>A result can carry warnings (for example that the substitution rule fell back to the ordinary ranking).</summary>
public class ResultNoticeTests : E2EBase
{
    [Test]
    public async Task Notice_IsShownAsAWarning_AboveTheSequences_AndOnlyForThatVariant()
    {
        await GotoAsync("/");
        await RunAsync("NOTICE_VARIANT, NC_000017.11:g.7674220C>T");
        await Expect(DoneStatus).ToContainTextAsync("2 of 2");

        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("NOTICE_VARIANT") }).ClickAsync();
        var notice = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "could not be applied" });
        await Expect(notice).ToBeVisibleAsync();
        await Expect(notice).ToContainTextAsync("ordinary ranking is shown instead");
        // The ranked tables are still there (the fallback shows candidates, not an empty table).
        await Expect(Page.GetByRole(AriaRole.Table).First.Locator("tbody tr")).ToHaveCountAsync(5);

        await Page.GetByRole(AriaRole.Tab, new() { Name = "NC_000017.11:g.7674220C>T" }).ClickAsync();
        await Expect(Page.GetByText("could not be applied")).ToHaveCountAsync(0);
    }
}
