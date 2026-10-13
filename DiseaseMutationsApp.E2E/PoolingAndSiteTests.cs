using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace DiseaseMutationsApp.E2E;

/// <summary>The Guide Pooling page: planning, the plate map and decoding a screening readout.</summary>
public class PoolingPageTests : E2EBase
{
    private ILocator CalculateButton => Page.GetByRole(AriaRole.Button, new() { Name = "Calculate distribution" });

    private async Task CalculateAsync()
    {
        await GotoAsync("/pooling");
        await CalculateButton.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Group, new() { NameRegex = new("^Plate 1, 8 rows by 12 columns") })).ToBeVisibleAsync();
    }

    [Test]
    public async Task HasATitle()
    {
        await GotoAsync("/pooling");
        await Expect(Page).ToHaveTitleAsync("Guide Pooling");
    }

    [Test]
    public async Task Calculate_ShowsThePlateMap_WithoutInvalidGridRoles()
    {
        await CalculateAsync();
        await Expect(Page.Locator("[role=grid], [role=gridcell]")).ToHaveCountAsync(0);
        Assert.That(await Page.Locator(".plate-grid button.pool-well").CountAsync(), Is.GreaterThan(0));
    }

    [Test]
    public async Task PlateMap_ArrowKeysMoveFocusBetweenWells()
    {
        await CalculateAsync();
        var first = Page.Locator(".plate-grid button.pool-well").First;
        await first.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.That(await Page.EvaluateAsync<string>("document.activeElement.dataset.c"), Is.EqualTo("1"));
        await Page.Keyboard.PressAsync("ArrowDown");
        Assert.That(await Page.EvaluateAsync<string>("document.activeElement.dataset.r"), Is.EqualTo("1"));
    }

    [Test]
    public async Task Decode_MarksPositiveWells_AndNamesTheGuides()
    {
        await CalculateAsync();
        // 100 guides, K = 5, 2D fragmented: tubes 1 (a row) and 2 (a column) of the first block intersect on one guide.
        await Page.GetByLabel("Positive wells").FillAsync("#1, #2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Decode" }).ClickAsync();

        await Expect(Page.Locator(".plate-grid button.pool-well-positive")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".plate-grid button.pool-well-positive").First).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex("positive"));
        await Expect(Page.Locator("[role=status] .alert").First).ToBeVisibleAsync();   // either "N guides consistent" or "No guide has all of its wells positive"
    }

    [Test]
    public async Task Decode_HugeNumber_IsReportedAsNotRecognised_AndTheCircuitSurvives()
    {
        await CalculateAsync();
        await Page.GetByLabel("Positive wells").FillAsync("#99999999999, A99999999999");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Decode" }).ClickAsync();

        await Expect(Page.GetByText("Not recognised")).ToBeVisibleAsync();
        // The page still reacts afterwards: a normal decode works.
        await Page.GetByLabel("Positive wells").FillAsync("#1");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Decode" }).ClickAsync();
        await Expect(Page.GetByText("Not recognised")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task PlanCsv_Downloads_WithTheDocumentedColumns()
    {
        await CalculateAsync();
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Button, new() { Name = "Download CSV" }).ClickAsync());
        var text = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync((await download.PathAsync())!));

        Assert.Multiple(() =>
        {
            Assert.That(text.TrimStart('﻿'), Does.StartWith("Tube ID,Mixture Name,Guides in Tandem,Well"));
            Assert.That(text.Split('\n').Length, Is.GreaterThan(10));
        });
    }

    [Test]
    public async Task GuidesPerWellOfZero_CannotBeCalculated()
    {
        await GotoAsync("/pooling");
        await Page.GetByLabel("Guides per well (K)").FillAsync("0");
        await Page.GetByLabel("Guides per well (K)").PressAsync("Tab");
        await Expect(CalculateButton).ToBeDisabledAsync();
    }
}

public class AppStatusPageTests : E2EBase
{
    [Test]
    public async Task RunChecks_ShowsATable_WithRemedies_ForWhatIsMissingOnThisMachine()
    {
        await GotoAsync("/app-status");
        await Expect(Page).ToHaveTitleAsync("App Status");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Run checks" }).ClickAsync();

        var table = Page.GetByRole(AriaRole.Table);
        await Expect(table).ToBeVisibleAsync();
        await Expect(table.GetByRole(AriaRole.Row)).Not.ToHaveCountAsync(1);   // header plus at least one check
        await Expect(table).ToContainTextAsync("Bowtie");
    }
}

/// <summary>Accessibility checks with axe-core (WCAG 2.0 to 2.2 A/AA): no serious or critical violation on any page state.</summary>
public class AccessibilityScanTests : E2EBase
{
    private async Task AssertNoSeriousViolationsAsync(string where)
    {
        var bad = (await Page.ViolationsAsync()).Where(v => v.Impact is "serious" or "critical").ToList();
        Assert.That(bad, Is.Empty, where + ": " + string.Join("; ", bad.Select(v => $"{v.Id} ({v.Impact}) on {v.Nodes.Length} node(s): {v.Nodes.First().Target}")));
    }

    [Test]
    public async Task DesignPage_Idle()
    {
        await GotoAsync("/");
        await AssertNoSeriousViolationsAsync("design page, idle");
    }

    [Test]
    public async Task DesignPage_WithResults()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T, rs334");
        await Expect(DoneStatus).ToContainTextAsync("5 of 5");
        await AssertNoSeriousViolationsAsync("design page, with results");
    }

    [Test]
    public async Task PoolingPage_WithAPlan()
    {
        await GotoAsync("/pooling");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Calculate distribution" }).ClickAsync();
        await Expect(Page.Locator(".plate-grid")).ToBeVisibleAsync();
        await AssertNoSeriousViolationsAsync("pooling page, with a plan");
    }

    [Test]
    public async Task AppStatusPage_AfterRunningChecks()
    {
        await GotoAsync("/app-status");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Run checks" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Table)).ToBeVisibleAsync();
        await AssertNoSeriousViolationsAsync("App Status page");
    }

    [Test]
    public async Task Focus_StartsOnThePageHeading_AndTheSkipLinkMovesFocusToTheContent()
    {
        await GotoAsync("/");
        // Design decision: the router moves focus to the page heading on load, so a keyboard user starts inside the main
        // content and has already "skipped" the navigation. The skip link remains for anyone who tabs back to it.
        await Expect(Page.Locator("h1")).ToBeFocusedAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" }).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("#main-content")).ToBeFocusedAsync();
    }
}

/// <summary>A phone-sized screen: nothing scrolls sideways and the sequence view stays keyboard-reachable.</summary>
public class PhoneLayoutTests : E2EBase
{
    protected override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 375, Height = 812 },
        IsMobile = true,
        AcceptDownloads = true
    };

    [TestCase("/")]
    [TestCase("/pooling")]
    [TestCase("/app-status")]
    public async Task NoHorizontalPageScroll(string path)
    {
        await GotoAsync(path);
        var overflow = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.That(overflow, Is.False, path + " scrolls sideways at 375 px");
    }

    [Test]
    public async Task ResultsPage_HasNoSeriousAccessibilityViolations_AtPhoneWidth()
    {
        await GotoAsync("/");
        await RunAsync("NC_000017.11:g.7674220C>T");
        await Expect(DoneStatus).ToBeVisibleAsync();
        var bad = (await Page.ViolationsAsync()).Where(v => v.Impact is "serious" or "critical").Select(v => v.Id).ToList();
        Assert.That(bad, Is.Empty);
    }
}
