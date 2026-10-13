using System.Text.Json;
using Bunit;
using DiseaseMutationsApp.Components;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Regression tests for the accessibility defects found with axe-core 4.10.2 on 2026-10-13
/// (docs/review-findings.md, A2-A5). They assert the markup facts axe checks, so they run without a browser.
/// </summary>
public class AccessibilityTests : BunitContext
{
    public AccessibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new GrnaService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService()));
    }

    private static PoolingPlanDto Plan()
    {
        var wells = new List<PoolWell>
        {
            new() { Id = 1, Name = "Row 1", GuideIndices = [1, 2], GuideLabels = ["G1", "G2"], WellLabel = "Plate 1 - A1", Plate = 1, Row = 'A', Column = 1 },
            new() { Id = 2, Name = "Row 2", GuideIndices = [], GuideLabels = [], WellLabel = "Plate 1 - A2", Plate = 1, Row = 'A', Column = 2 },
            new() { Id = 3, Name = "Row 3", GuideIndices = [3], GuideLabels = ["G3"], WellLabel = "Plate 1 - B1", Plate = 1, Row = 'B', Column = 1 },
        };
        return new PoolingPlanDto
        {
            Model = PoolingModelKind.TwoDMatrix, ModelName = "2D Matrix", GuideCount = 3, WellCapacity = 5, Repetitions = 2,
            Pools = wells, TotalWells = 3, NonEmptyWells = 2, MaxPoolSize = 2, PlateRows = 8, PlateColumns = 12, PlateCount = 1
        };
    }

    // A2: the plate map had role="grid" with 96 gridcell buttons and no rows (axe: aria-required-children / -parent).
    [Test]
    public void PlateMap_UsesNoGridRoles_AndKeepsOneButtonPerUsedWell()
    {
        var cut = Render<PoolPlateMap>(p => p.Add(c => c.Plan, Plan()));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("[role=grid]"), Is.Empty);
            Assert.That(cut.FindAll("[role=gridcell]"), Is.Empty);
            Assert.That(cut.Find(".plate-grid").GetAttribute("role"), Is.EqualTo("group"));
            Assert.That(cut.FindAll("button.pool-well"), Has.Count.EqualTo(3));
            Assert.That(cut.FindAll("button.pool-well").All(b => b.HasAttribute("data-r") && b.HasAttribute("data-c")), Is.True,
                "arrow-key navigation is driven by data-r / data-c");
        });
    }

    [Test]
    public void PlateMap_UnusedWells_AreHiddenFromAssistiveTechnology()
    {
        var cut = Render<PoolPlateMap>(p => p.Add(c => c.Plan, Plan()));
        var unused = cut.FindAll(".plate-grid .pool-well-unused");
        Assert.Multiple(() =>
        {
            Assert.That(unused, Is.Not.Empty);
            Assert.That(unused.All(u => u.GetAttribute("aria-hidden") == "true"), Is.True);
        });
    }

    // aria-pressed on a gridcell was invalid; on a button it is the correct way to say "selected".
    [Test]
    public void PlateMap_SelectedWell_IsAPressedButton()
    {
        var cut = Render<PoolPlateMap>(p => p.Add(c => c.Plan, Plan()).Add(c => c.SelectedPoolId, 3));
        var pressed = cut.FindAll("button.pool-well").Where(b => b.GetAttribute("aria-pressed") == "true").ToList();
        Assert.That(pressed, Has.Count.EqualTo(1));
        Assert.That(pressed[0].GetAttribute("aria-label"), Does.Contain("tube 3"));
    }

    // A5: positive wells were never marked; the mark must not rely on colour alone.
    [Test]
    public void PlateMap_PositiveWells_AreMarkedAndAnnounced()
    {
        var cut = Render<PoolPlateMap>(p => p
            .Add(c => c.Plan, Plan())
            .Add(c => c.PositiveIds, new HashSet<int> { 1 }));

        var positive = cut.FindAll("button.pool-well-positive");
        Assert.Multiple(() =>
        {
            Assert.That(positive, Has.Count.EqualTo(1));
            Assert.That(positive[0].GetAttribute("aria-label"), Does.Contain("positive"));
            Assert.That(cut.Markup, Does.Contain("Positive"), "the legend names the state");
        });
    }

    [Test]
    public void PlateMap_AnnouncesOnFocus_NotOnHover()
    {
        var cut = Render<PoolPlateMap>(p => p.Add(c => c.Plan, Plan()));
        var live = () => cut.Find(".visually-hidden[aria-live=polite]").TextContent;
        Assert.That(live(), Is.Empty);

        cut.FindAll("button.pool-well")[0].MouseEnter();
        Assert.That(live(), Is.Empty, "hovering must not be announced");

        cut.FindAll("button.pool-well")[0].Focus();
        Assert.That(live(), Does.Contain("Plate 1 - A1").And.Contain("G1"));
    }

    [Test]
    public void PlateMap_HoverAndFocusLiveRegionIsNotTheVisibleReadout()
    {
        var cut = Render<PoolPlateMap>(p => p.Add(c => c.Plan, Plan()));
        Assert.That(cut.Find(".pool-readout").HasAttribute("aria-live"), Is.False);
    }

    // A4: only the selected tab may point at a panel, because only that panel is rendered.
    [Test]
    public void TabStrip_OnlyTheSelectedTab_HasAriaControls()
    {
        var cut = Render<TabStrip<string>>(p => p
            .Add(c => c.Items, new[] { "a", "b", "c" })
            .Add(c => c.KeyOf, s => s)
            .Add(c => c.ActiveKey, "b")
            .Add(c => c.IdPrefix, "t")
            .Add(c => c.ItemTemplate, (RenderFragment<string>)(s => b => b.AddContent(0, s))));

        var tabs = cut.FindAll("[role=tab]");
        Assert.That(tabs.Select(t => t.HasAttribute("aria-controls")), Is.EqualTo(new[] { false, true, false }));
    }

    // A3: a scrollable region must be reachable by keyboard (axe: scrollable-region-focusable).
    [Test]
    public void SequenceView_IsKeyboardFocusable()
    {
        var cut = Render<SequenceView>(p => p.Add(c => c.Sequence, "ACGTACGTAC").Add(c => c.Title, "Original sequence"));
        var view = cut.Find(".seq-view");
        Assert.Multiple(() =>
        {
            Assert.That(view.GetAttribute("tabindex"), Is.EqualTo("0"));
            Assert.That(view.GetAttribute("aria-label"), Is.EqualTo("Original sequence"));
        });
    }

    // A4: two tables on one page must not both expose a landmark called "Filter candidates".
    [Test]
    public void ResultsTables_HaveDistinctSearchLandmarks()
    {
        string LabelFor(string title)
        {
            var cut = Render<GrnaResultsTable>(p => p.Add(c => c.GRNAs, new List<GRNAResult> { Candidate() }).Add(c => c.Title, title));
            return cut.Find("[role=search]").GetAttribute("aria-label")!;
        }

        Assert.That(LabelFor("gRNA spacer results: mutated sequence"), Is.Not.EqualTo(LabelFor("gRNA spacer results: original sequence")));
    }

    private static GRNAResult Candidate() => new()
    {
        Sequence = "AGUUCCUGCAUGGGCGGCAUGAACAAGA", SeedRegion = "UGGGCGGC", Score = 1, Allignments = 1,
        RnaFoldResult = new RNAFoldResult { Structure = "....", Energy = -1.0 }, MutationHighlightStart = -1
    };

    // S2: /healthz details contain file-system paths; only loopback callers get them.
    [Test]
    public void Healthz_NonLoopback_GetsVerdictAndStatusOnly()
    {
        var report = new DiagnosticsReport(true, DateTimeOffset.UtcNow, new List<DiagnosticCheck>
        {
            new("Bowtie index", "pass", "/app/bowtie/indexes/GRCh38_noalt_as [Bowtie 2 (.bt2)]", ""),
            new("ViennaRNA", "fail", "python3 missing at /usr/bin/python3", "pip install viennarna==2.7.2")
        });

        var remote = JsonSerializer.Serialize(DiagnosticsService.HealthzPayload(report, isLoopback: false));
        var local = JsonSerializer.Serialize(DiagnosticsService.HealthzPayload(report, isLoopback: true));

        Assert.Multiple(() =>
        {
            Assert.That(remote, Does.Contain("Bowtie index").And.Contain("pass").And.Contain("healthy"));
            Assert.That(remote, Does.Not.Contain("/app/").And.Not.Contain("/usr/bin").And.Not.Contain("pip install"));
            Assert.That(local, Does.Contain("/app/bowtie/indexes").And.Contain("pip install"));
        });
    }
}
