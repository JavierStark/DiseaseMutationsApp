using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace DiseaseMutationsApp.E2E;

/// <summary>Shared steps for the Disease variants page (served over <see cref="FakeDiseaseSource"/>).</summary>
public abstract class DiseaseE2EBase : E2EBase
{
    protected ILocator DiseaseBox => Page.GetByLabel("Disease name, OMIM number or MONDO id");
    protected ILocator FindDiseasesButton => Page.GetByRole(AriaRole.Button, new() { Name = "Find diseases" });
    protected ILocator FindVariantsButton => Page.GetByRole(AriaRole.Button, new() { Name = "Find variants" });
    protected ILocator Rows => Page.Locator("tbody tr");
    protected ILocator Status => Page.Locator("#disease-status");
    protected ILocator SelectionBar => Page.GetByRole(AriaRole.Region, new() { Name = "Selection" });
    protected ILocator NavLink(string name) => Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    protected async Task FindDiseasesAsync(string text)
    {
        await DiseaseBox.FillAsync(text);
        await FindDiseasesButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Radio).First).ToBeVisibleAsync();
    }

    protected async Task FindVariantsAsync(int expectedRows)
    {
        await FindVariantsButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Table)).ToBeVisibleAsync();
        await Expect(Rows).ToHaveCountAsync(expectedRows);
    }

    /// <summary>Opens the page, searches and reads the variants of the first match.</summary>
    protected async Task OpenWithResultsAsync(string text, int expectedRows)
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync(text);
        await FindVariantsAsync(expectedRows);
    }

    protected ILocator RowBox(string label) => Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {label}" });
    protected ILocator SelectionButton(string name) => SelectionBar.GetByRole(AriaRole.Button, new() { Name = name });
    protected ILocator DianaBox => Page.GetByLabel("HGVS or rs codes", new() { Exact = false });
}

/// <summary>Find a disease, read its variants, pick some and hand them to Diana.</summary>
public class DiseasePageTests : DiseaseE2EBase
{
    [Test]
    public async Task PageHasATitle_AnH1_AndAnEntryInTheNavigation()
    {
        await GotoAsync("/disease");

        await Expect(Page).ToHaveTitleAsync("Disease variants");
        await Expect(Page.Locator("main h1")).ToHaveCountAsync(1);
        await Expect(NavLink("Disease variants")).ToHaveAttributeAsync("aria-current", "page");
    }

    [Test]
    public async Task TheDianaPage_LinksToIt()
    {
        await GotoAsync("/");
        await NavLink("Disease variants").ClickAsync();

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Disease variants");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/disease$"));
    }

    [Test]
    public async Task Permalink_FillsTheBox_ButNeverSearches()
    {
        await GotoAsync("/disease?q=sickle%20cell%20disease");

        await Expect(DiseaseBox).ToHaveValueAsync("sickle cell disease");
        await Expect(Page.GetByText("opened from a link")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.GetByRole(AriaRole.Radio)).ToHaveCountAsync(0);   // opening the link searched nothing

        await FindDiseasesButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Radio)).ToHaveCountAsync(2);
    }

    // Regression: with Bootstrap's floated .form-check the box collapsed to one line while its two-line label overflowed, so the
    // rows overlapped. No unit test could see it; this measures the real layout.
    [TestCase(1280)]
    [TestCase(375)]
    public async Task MatchRows_ContainTheirText_AndDoNotOverlap(int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await GotoAsync("/disease");
        await FindDiseasesAsync("manymatches");   // ten rows: enough to need the 20 rem scroll list, where flex items shrink

        var boxes = await Page.Locator(".match-item").EvaluateAllAsync<double[][]>(
            "els => els.map(e => { const r = e.getBoundingClientRect(); const l = e.querySelector('label').getBoundingClientRect(); const i = e.querySelector('input').getBoundingClientRect(); return [r.top, r.bottom, l.bottom, i.left - r.left, l.left - i.right, r.right, l.right]; })");

        Assert.That(boxes, Has.Length.EqualTo(10));
        Assert.Multiple(() =>
        {
            foreach (var b in boxes)
            {
                Assert.That(b[2], Is.LessThanOrEqualTo(b[1] + 0.5), "the label stays inside its row");
                Assert.That(b[3], Is.GreaterThanOrEqualTo(0), "the radio is inside its row");
                Assert.That(b[4], Is.GreaterThan(0), "the label starts to the right of the radio");
                Assert.That(b[6], Is.LessThanOrEqualTo(b[5] + 0.5), "the label does not spill out sideways");
            }
            for (var i = 1; i < boxes.Length; i++)
                Assert.That(boxes[i][0], Is.GreaterThanOrEqualTo(boxes[i - 1][1] - 0.5), $"row {i + 1} starts below row {i}");
        });
    }

    [Test]
    public async Task Search_ChooseAndRead_ShowsTheSummaryAndTheTable()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("sickle cell");

        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "sickle cell disease" })).ToBeCheckedAsync();
        await FindVariantsAsync(8);

        await Expect(Page.GetByText("8 ClinVar records classified pathogenic or likely pathogenic for OMIM 603903")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "rs334 (opens dbSNP)" })).ToHaveAttributeAsync("href", "https://www.ncbi.nlm.nih.gov/snp/rs334");
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Gene" })).ToHaveAttributeAsync("aria-sort", "none");
    }

    [Test]
    public async Task SelectThree_SendToDiana_FillsTheBuilderBox_AndRunningWorks()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();
        await RowBox("rs33930165").CheckAsync();
        await RowBox("rs33950507").CheckAsync();
        await Expect(SelectionBar).ToContainTextAsync("3 selected");

        await SelectionButton("Send to Diana (replace)").ClickAsync();

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Diana");
        await Expect(DianaBox).ToHaveValueAsync("rs334, rs33930165, rs33950507");
        await Expect(Page.GetByText("Disease variants: 3 inputs put in the box.")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(800);
        await Expect(Page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(0);   // arriving started nothing

        await RunButton.ClickAsync();
        await Expect(DoneStatus).ToContainTextAsync("12 of 12 variants analysed");   // 3 rsIDs x 2 notations x 2 strands (fake pipeline)
        await Expect(Page.GetByText("Disease variants: 3 inputs put in the box.")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task AddToDianaInputs_KeepsWhatWasThere()
    {
        await GotoAsync("/");
        await DianaBox.FillAsync("NC_000017.11:g.7674220C>T, rs334");
        await NavLink("Disease variants").ClickAsync();
        await FindDiseasesAsync("sickle cell");
        await FindVariantsAsync(8);
        await RowBox("rs334").CheckAsync();               // already in the box
        await RowBox("rs33930165").CheckAsync();

        await SelectionButton("Add to Diana inputs").ClickAsync();

        await Expect(DianaBox).ToHaveValueAsync("NC_000017.11:g.7674220C>T, rs334, rs33930165");
        await Expect(Page.GetByText("1 input added (1 already there), 3 in total.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TheRowButton_SendsOneVariant_AndAnHgvsOnlyRowSendsItsHgvs()
    {
        await OpenWithResultsAsync("sickle cell", 8);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Send rs334 to Diana" }).ClickAsync();
        await Expect(DianaBox).ToHaveValueAsync("rs334");

        await NavLink("Disease variants").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send NC_000011.10:g.5225715G>C to Diana" }).ClickAsync();
        await Expect(DianaBox).ToHaveValueAsync("rs334, NC_000011.10:g.5225715G>C");
    }

    [Test]
    public async Task ARowWithNothingToSend_CannotBeSelected()
    {
        await OpenWithResultsAsync("sickle cell", 8);

        await Expect(Page.GetByRole(AriaRole.Checkbox, new() { Name = "a variant with nothing to send has no rsID or HGVS to send" })).ToBeDisabledAsync();
    }

    [Test]
    public async Task AGroupDisease_NeedsItsSubtypes_AndMoreThan50Inputs_CannotBeSent()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("fanconi");
        await Expect(Page.GetByLabel("Also search its subtypes")).ToBeCheckedAsync();
        await Expect(Page.GetByLabel("Also search its subtypes")).ToBeDisabledAsync();
        await FindVariantsAsync(25);   // 70 variants, 25 per page
        await Expect(Page.GetByText("Consulted 22 of 22 subtypes")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select all 70 matching" }).ClickAsync();

        await Expect(SelectionBar).ToContainTextAsync("70 selected");
        await Expect(SelectionBar.GetByRole(AriaRole.Alert)).ToContainTextAsync("at most 50");
        await Expect(SelectionButton("Send to Diana (replace)")).ToBeDisabledAsync();
        await Expect(SelectionButton("Add to Diana inputs")).ToBeDisabledAsync();

        // The list can still be taken away for `grna design --input`.
        var download = await Page.RunAndWaitForDownloadAsync(async () => await SelectionButton("Download list (.txt)").ClickAsync());
        var lines = (await File.ReadAllTextAsync((await download.PathAsync())!)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(download.SuggestedFilename, Does.StartWith("Disease_Fanconi_anemia_selected_").And.EndWith(".txt"));
            Assert.That(lines, Has.Length.EqualTo(70));
            Assert.That(lines.Count(l => l.StartsWith("rs")), Is.EqualTo(60));
            Assert.That(lines.Count(l => l.StartsWith("NC_")), Is.EqualTo(10), "rows without an rsID contribute their HGVS");
        });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await Expect(SelectionBar).ToHaveCountAsync(0);
    }

    [Test]
    public async Task FilterAndPaging_WorkTogether()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("fanconi");
        await FindVariantsAsync(25);
        await Expect(Page.GetByText("Page 1 of 3")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.GetByText("Page 2 of 3")).ToBeVisibleAsync();

        await Page.GetByLabel("Only with an rsID").CheckAsync();
        await Expect(Page.GetByText("Page 1 of 3")).ToBeVisibleAsync();   // a new filter returns to the first page
        await Expect(Page.GetByText("filtered from 70")).ToBeVisibleAsync();

        await Page.GetByLabel("Contains").FillAsync("rs500001");
        await Expect(Rows).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Sorting_ByGene_ReordersTheRows()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("fanconi");
        await FindVariantsAsync(25);

        var gene = Page.GetByRole(AriaRole.Columnheader, new() { Name = "Gene" });
        await gene.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(gene).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(Rows.First).ToContainTextAsync("GENE0");

        await gene.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(gene).ToHaveAttributeAsync("aria-sort", "descending");
        await Expect(Rows.First).ToContainTextAsync("GENE4");
    }

    [Test]
    public async Task KeyboardOnly_SelectARowAndSendIt()
    {
        await OpenWithResultsAsync("sickle cell", 8);

        await RowBox("rs334").FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(SelectionBar).ToContainTextAsync("1 selected");

        await SelectionButton("Send to Diana (replace)").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        await Expect(DianaBox).ToHaveValueAsync("rs334");
    }

    [Test]
    public async Task DownloadAllRsIds_ListsEveryDistinctRsId()
    {
        await OpenWithResultsAsync("sickle cell", 8);

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "All rsIDs (.txt)" }).ClickAsync());
        var text = await File.ReadAllTextAsync((await download.PathAsync())!);

        Assert.Multiple(() =>
        {
            Assert.That(download.SuggestedFilename, Does.StartWith("Disease_sickle_cell_disease_rsids_").And.EndWith(".txt"));
            Assert.That(text.Split('\n', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[] { "rs334", "rs33930165", "rs33950507", "rs33946267", "rs33971440", "rs11549407" }));
        });
    }

    [Test]
    public async Task DownloadTable_IsACsvWithABom()
    {
        await OpenWithResultsAsync("sickle cell", 8);

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "Table (.csv)" }).ClickAsync());
        var bytes = await File.ReadAllBytesAsync((await download.PathAsync())!);

        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3).ToArray(), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 byte-order mark for Excel");
            Assert.That(System.Text.Encoding.UTF8.GetString(bytes), Does.Contain("rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions").And.Contain("rs334,NC_000011.10:g.5227002T>A,HBB,Pathogenic"));
        });
    }

    [Test]
    public async Task TheResultAndSelection_SurviveVisitingTheDianaPage()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();

        await NavLink("Diana").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Diana");
        await NavLink("Disease variants").ClickAsync();

        await Expect(Rows).ToHaveCountAsync(8);
        await Expect(RowBox("rs334")).ToBeCheckedAsync();
        await Expect(SelectionBar).ToContainTextAsync("1 selected");
    }

    // ---- the unhappy paths ---------------------------------------------------------------------------

    [Test]
    public async Task NoMatch_SaysSo()
    {
        await GotoAsync("/disease");
        await DiseaseBox.FillAsync("nomatch");
        await FindDiseasesButton.ClickAsync();

        await Expect(Page.GetByText("No disease matched")).ToBeVisibleAsync();
    }

    [Test]
    public async Task MonarchDown_ShowsTheReason_AndTheTryAgainButton()
    {
        await GotoAsync("/disease");
        await DiseaseBox.FillAsync("down");
        await FindDiseasesButton.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Monarch could not be reached." })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Try again" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task AFailedVariantSearch_CanBeRetried()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("flaky");
        await FindVariantsButton.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "NCBI esearch returned HTTP 503." })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();

        await Expect(Rows).ToHaveCountAsync(5);
    }

    [Test]
    public async Task ADiseaseWithoutVariants_ExplainsWhy()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("empty");
        await FindVariantsButton.ClickAsync();

        await Expect(Page.GetByText("No variants at this classification")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AnIncompleteList_IsFlagged()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("trunc");
        await FindVariantsButton.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "The list is incomplete" })).ToContainTextAsync("4543 records matched");
    }

    [Test]
    public async Task CancelRun_StopsASlowSearch_AndThePageIsUsableAgain()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("slowvars");
        await FindVariantsButton.ClickAsync();
        await Expect(Status).ToContainTextAsync("Finding ClinVar records");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        await Expect(Status).ToContainTextAsync("Search cancelled.");
        await Expect(FindVariantsButton).ToBeEnabledAsync();
        await Expect(Page.GetByRole(AriaRole.Table)).ToHaveCountAsync(0);
    }
}

/// <summary>axe-core (WCAG 2.0 to 2.2 A/AA) over every state of the Disease variants page.</summary>
public class DiseaseAccessibilityTests : DiseaseE2EBase
{
    private async Task AssertNoSeriousViolationsAsync(string where)
    {
        var bad = (await Page.ViolationsAsync()).Where(v => v.Impact is "serious" or "critical").ToList();
        Assert.That(bad, Is.Empty, where + ": " + string.Join("; ", bad.Select(v => $"{v.Id} ({v.Impact}) on {v.Nodes.Length} node(s): {v.Nodes.First().Target}")));
    }

    [Test]
    public async Task Idle()
    {
        await GotoAsync("/disease");
        await AssertNoSeriousViolationsAsync("disease page, idle");
    }

    [Test]
    public async Task WithMatches()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("fanconi");
        await AssertNoSeriousViolationsAsync("disease page, with matches");
    }

    [Test]
    public async Task WithResultsAndASelection()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();
        await RowBox("rs33930165").CheckAsync();
        await AssertNoSeriousViolationsAsync("disease page, results and selection");
    }

    [Test]
    public async Task WithTheOverLimitMessage()
    {
        await GotoAsync("/disease");
        await FindDiseasesAsync("fanconi");
        await FindVariantsAsync(25);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select all 70 matching" }).ClickAsync();
        await Expect(SelectionBar.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await AssertNoSeriousViolationsAsync("disease page, over the run limit");
    }

    [Test]
    public async Task WithAnError()
    {
        await GotoAsync("/disease");
        await DiseaseBox.FillAsync("down");
        await FindDiseasesButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Try again" })).ToBeVisibleAsync();
        await AssertNoSeriousViolationsAsync("disease page, with an error");
    }

    [Test]
    public async Task DianaPage_WithTheIncomingBanner()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();
        await SelectionButton("Send to Diana (replace)").ClickAsync();
        await Expect(Page.GetByText("Disease variants: 1 input put in the box.")).ToBeVisibleAsync();
        await AssertNoSeriousViolationsAsync("Diana page, with the banner");
    }
}

/// <summary>A phone-sized screen: nothing scrolls sideways, the action bar stays reachable.</summary>
public class DiseasePhoneTests : DiseaseE2EBase
{
    protected override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 375, Height = 812 },
        IsMobile = true,
        AcceptDownloads = true
    };

    private async Task AssertNoSidewaysScrollAsync(string where)
    {
        // Not window.innerWidth: in mobile emulation the browser widens the layout viewport to fit overflowing content, which
        // would hide exactly the problem being tested for.
        var width = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        Assert.That(width, Is.LessThanOrEqualTo(376), where + " is wider than a 375 px screen");
    }

    [Test]
    public async Task Idle_AndWithMatches_AndWithResults_DoNotScrollSideways()
    {
        await GotoAsync("/disease");
        await AssertNoSidewaysScrollAsync("idle");
        await FindDiseasesAsync("fanconi");
        await AssertNoSidewaysScrollAsync("matches");
        await FindVariantsAsync(25);
        await AssertNoSidewaysScrollAsync("results");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select all 70 matching" }).ClickAsync();
        await AssertNoSidewaysScrollAsync("results with the selection bar");
    }

    [Test]
    public async Task SendingFromAPhone_Works()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();
        var send = SelectionButton("Send to Diana (replace)");
        await send.ScrollIntoViewIfNeededAsync();
        await send.ClickAsync();

        await Expect(DianaBox).ToHaveValueAsync("rs334");
    }

    [Test]
    public async Task ResultsAtPhoneWidth_HaveNoSeriousAccessibilityViolations()
    {
        await OpenWithResultsAsync("sickle cell", 8);
        await RowBox("rs334").CheckAsync();
        var bad = (await Page.ViolationsAsync()).Where(v => v.Impact is "serious" or "critical").Select(v => v.Id).ToList();
        Assert.That(bad, Is.Empty);
    }
}
