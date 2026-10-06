using Microsoft.Extensions.DependencyInjection;
using Bunit;
using DiseaseMutationsApp.Components;
using DiseaseMutationsApp.Pages;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;

namespace DiseaseMutationsAppTests;

public class AlignmentRiskTests
{
    [TestCase(0, "risk-none", "0 none")]
    [TestCase(1, "risk-unique", "1 unique")]
    [TestCase(2, "risk-caution", "2 caution")]
    [TestCase(3, "risk-caution", "3 caution")]
    [TestCase(4, "risk-high", "4 high")]
    [TestCase(5, "risk-high", "5 high")]
    [TestCase(6, "risk-saturated", "6+ saturated")]
    [TestCase(600, "risk-saturated", "6+ saturated")]
    public void Describe_LabelsCountWithText(int count, string css, string label)
    {
        var (c, l) = AlignmentRisk.Describe(count);
        Assert.Multiple(() =>
        {
            Assert.That(c, Is.EqualTo(css));
            Assert.That(l, Is.EqualTo(label));
        });
    }
}

public class SequenceHighlightTests
{
    [Test]
    public void Highlight_SplitsIntoRunsWithSeedAndMutationFlags()
    {
        var segs = SequenceSpan.Highlight("AAAACCCCGGGG", 2, 5, 4, 2);
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(segs.Select(s => s.Text)), Is.EqualTo("AAAACCCCGGGG"));
            Assert.That(segs.Select(s => (s.Text, s.Seed, s.Mutation)), Is.EqualTo(new[]
            {
                ("AA", false, false), ("AA", true, false), ("CC", true, true), ("CCGGGG", false, false)
            }));
        });
    }

    [Test]
    public void Highlight_OutOfRangeMutationAndEmptySequence_AreSafe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SequenceSpan.Highlight("", 0, 1, 0, 1), Is.Empty);
            Assert.That(SequenceSpan.Highlight("ACGT", null, null, 10, 2).Single().Mutation, Is.False);
            Assert.That(SequenceSpan.Highlight("ACGT", 3, 1, null, null).Single().Seed, Is.False, "inverted seed highlights nothing");
        });
    }
}

public class ComponentTests : BunitContext
{
    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new GrnaService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService()));
        var state = new AppStateService();
        Services.AddSingleton(state);
        Services.AddSingleton(new AnalysisRunner(new FakeGrnaAnalysis(), state,
            Microsoft.Extensions.Options.Options.Create(new AnalysisOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalysisRunner>.Instance));
    }

    private sealed class FakeGrnaAnalysis : IGrnaAnalysis
    {
        public Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? GetNcbiNuccoreUrl(string hgvs) => null;
    }

    [Test]
    public void TabStatusLabel_ConveysStateByTextAndAriaLabel_NotColourAlone()
    {
        var cut = Render<TabStatusLabel>(p => p
            .Add(c => c.Label, "NG_1.1:g.5A>T")
            .Add(c => c.State, TabState.Failed)
            .Add(c => c.IsComplement, true));

        var root = cut.Find(".tab-status");
        Assert.Multiple(() =>
        {
            Assert.That(root.GetAttribute("aria-label"), Is.EqualTo("NG_1.1:g.5A>T, complement strand: failed"));
            Assert.That(cut.Find(".visually-hidden").TextContent, Is.EqualTo("failed"));
            Assert.That(root.ClassList, Does.Contain("status-error"));
            Assert.That(cut.Find(".status-shape"), Is.Not.Null, "a distinct shape accompanies the state");
        });
    }

    [Test]
    public void SequenceSpan_EscapesMarkupInTheSequence()
    {
        var cut = Render<SequenceSpan>(p => p.Add(c => c.Sequence, "<b>x</b>"));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("&lt;b&gt;"));
            Assert.That(cut.FindAll("b"), Is.Empty);
        });
    }

    [Test]
    public void SequenceView_RendersRowsOf60_InBlocksOf10_WithCoordinates()
    {
        var seq = string.Concat(Enumerable.Repeat("ACGTACGTAC", 13)); // 130 nt
        var cut = Render<SequenceView>(p => p
            .Add(c => c.Sequence, seq)
            .Add(c => c.MutationStart, 12)
            .Add(c => c.MutationLength, 3));

        var gutters = cut.FindAll(".seq-gutter").Select(g => g.TextContent).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(gutters, Is.EqualTo(new[] { "1", "61", "121" }));
            Assert.That(cut.FindAll(".seq-row")[0].QuerySelectorAll(".seq-block"), Has.Length.EqualTo(6));
            Assert.That(cut.FindAll(".seq-view .seq-mut").Select(m => m.TextContent).Aggregate("", (a, b) => a + b), Is.EqualTo("GTA"));
        });
    }

    [Test]
    public void SequenceView_CollapsesLongSequences_UntilExpanded()
    {
        var seq = new string('A', 60 * 10);
        var cut = Render<SequenceView>(p => p.Add(c => c.Sequence, seq));
        Assert.That(cut.FindAll(".seq-row"), Has.Count.EqualTo(6));

        cut.FindAll("button").First(b => b.TextContent.Contains("Show all")).Click();

        Assert.That(cut.FindAll(".seq-row"), Has.Count.EqualTo(10));
    }

    private static GRNAResult G(string seq, double score, int aln, double energy) => new()
    {
        Sequence = seq, SeedRegion = seq[..4], Score = score, Allignments = aln, GCContent = 50, GCScore = 1,
        RnaFoldResult = new RNAFoldResult { Structure = "....", Energy = energy }, MutationHighlightStart = -1
    };

    [Test]
    public void GrnaResultsTable_HeadersAreButtonsWithAriaSort_AndRowsCarryRiskText()
    {
        var items = new List<GRNAResult> { G("AAAACCCCGGGGUUUU", 0.9, 1, -2), G("CCCCGGGGUUUUAAAA", 0.5, 6, -1) };
        var sorted = new List<GrnaSortColumn>();
        var cut = Render<GrnaResultsTable>(p => p
            .Add(c => c.GRNAs, items)
            .Add(c => c.SortColumn, GrnaSortColumn.Score)
            .Add(c => c.SortAscending, false)
            .Add(c => c.OnSortRequested, EventCallback.Factory.Create<GrnaSortColumn>(this, c => sorted.Add(c))));

        var scoreHeader = cut.FindAll("th").Single(h => h.TextContent.Trim() == "Score");
        Assert.That(scoreHeader.GetAttribute("aria-sort"), Is.EqualTo("descending"));
        Assert.That(cut.FindAll("th").Single(h => h.TextContent.Contains("GC %")).GetAttribute("aria-sort"), Is.EqualTo("none"));

        scoreHeader.QuerySelector("button")!.Click();
        Assert.That(sorted, Is.EqualTo(new[] { GrnaSortColumn.Score }));

        var chips = cut.FindAll(".risk-chip").Select(c => c.TextContent.Trim()).ToList();
        Assert.That(chips, Is.EqualTo(new[] { "1 unique", "6+ saturated" }));
    }

    [Test]
    public void GrnaResultsTable_FlagsSpacersWhoseDnaHasARestrictionSite()
    {
        var items = new List<GRNAResult> { G("ACGUGAAUUCACGUACGUAC", 0.9, 1, -2), G("ACGUACGUACGUACGUACGU", 0.5, 1, -1) };
        var cut = Render<GrnaResultsTable>(p => p.Add(c => c.GRNAs, items));

        var chips = cut.FindAll(".site-warning");
        Assert.That(chips, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(chips[0].TextContent.Trim(), Is.EqualTo("EcoRI"));
            Assert.That(chips[0].GetAttribute("title"), Is.EqualTo("Warning: This DNA sequence includes a restriction site for the enzyme: 'EcoRI'"));
        });
    }

    private static HgvsData ReadyLeaf(bool complement, string? spacer = null) => new()
    {
        Hgvs = "NC_000017.11:g.100A>G", IsComplement = complement, Status = LeafStatus.Ready,
        Original = "ACGTACGTAC", Mutated = "ACGTGCGTAC", ExtraNucleotids = 4, WindowStart = 96, WindowEnd = 105, SelectedSpacer = spacer
    };

    [Test]
    public void HgvsDetailPanel_ShowsLocusAndStrand_OnNormalAndComplementCards()
    {
        var normal = Render<HgvsDetailPanel>(p => p.Add(c => c.Data, ReadyLeaf(false)));
        var complement = Render<HgvsDetailPanel>(p => p.Add(c => c.Data, ReadyLeaf(true)));

        var normalLines = normal.FindAll(".seq-locus").Select(l => l.TextContent).ToList();
        var complementLines = complement.FindAll(".seq-locus").Select(l => l.TextContent).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(normalLines, Has.Count.EqualTo(2), "original and mutated cards");
            Assert.That(normalLines, Has.All.Contain("NC_000017.11:96-105").And.Contain("+ strand").And.Contain("5'→3'"));
            Assert.That(complementLines, Has.All.Contain("NC_000017.11:96-105").And.Contain("− strand").And.Contain("3'→5'"));
        });
    }

    [Test]
    public void HgvsDetailPanel_CompleteGrnaWarnsWithTheExactMessage_OnlyWhenASiteIsPresent()
    {
        var flagged = Render<HgvsDetailPanel>(p => p.Add(c => c.Data, ReadyLeaf(false, "ACGUGAAUUCACGUACGUAC")));
        var clean = Render<HgvsDetailPanel>(p => p.Add(c => c.Data, ReadyLeaf(false, "ACGUACGUACGUACGUACGU")));

        Assert.Multiple(() =>
        {
            Assert.That(flagged.FindAll(".complete-grna-card .alert-warning").Select(a => a.TextContent.Trim()),
                Is.EqualTo(new[] { "Warning: This DNA sequence includes a restriction site for the enzyme: 'EcoRI'" }));
            Assert.That(clean.FindAll(".complete-grna-card .alert-warning"), Is.Empty);
        });
    }

    [Test]
    public void GrnaResultsTable_FilterHidesRows_AndToleratesMissingFoldResult()
    {
        var noFold = new GRNAResult { Sequence = "ACGUACGUACGUACGU", SeedRegion = "ACGU", Score = 0.2, Allignments = 0, RnaFoldResult = null!, MutationHighlightStart = -1 };
        var items = new List<GRNAResult> { G("AAAACCCCGGGGUUUU", 0.9, 1, -2), noFold };
        var filter = new GrnaFilter { MinScore = 0.5 };

        var cut = Render<GrnaResultsTable>(p => p.Add(c => c.GRNAs, items).Add(c => c.Filter, filter));
        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1));

        filter.MinScore = null;
        cut.Render();
        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2));
        Assert.That(cut.Markup, Does.Contain("n/a"));
    }

    [Test]
    public void GrnaResultsTable_Pages()
    {
        var items = Enumerable.Range(0, 30).Select(i => G("ACGU" + i.ToString("D12").Replace('0', 'A').Replace('1', 'C'), 0.5, 1, -1)).ToList();
        var filter = new GrnaFilter { PageSize = 10 };
        var cut = Render<GrnaResultsTable>(p => p.Add(c => c.GRNAs, items).Add(c => c.Filter, filter));

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(10));
        cut.FindAll("button").First(b => b.TextContent == "Next").Click();
        Assert.That(filter.Page, Is.EqualTo(1));
    }

    [Test]
    public void TabStrip_ExposesTablistSemantics_AndRovingTabindex()
    {
        var items = new[] { "a", "b", "c" };
        var cut = Render<TabStrip<string>>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.KeyOf, s => s)
            .Add(c => c.ActiveKey, "b")
            .Add(c => c.IdPrefix, "t")
            .Add(c => c.AriaLabel, "Letters")
            .Add(c => c.ItemTemplate, (RenderFragment<string>)(s => b => b.AddContent(0, s))));

        var tabs = cut.FindAll("[role=tab]");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("[role=tablist]").GetAttribute("aria-label"), Is.EqualTo("Letters"));
            Assert.That(tabs.Select(t => t.GetAttribute("aria-selected")), Is.EqualTo(new[] { "false", "true", "false" }));
            Assert.That(tabs.Select(t => t.GetAttribute("tabindex")), Is.EqualTo(new[] { "-1", "0", "-1" }));
            Assert.That(tabs[1].GetAttribute("aria-controls"), Is.EqualTo(TabStrip<string>.PanelId("t", "b")));
            Assert.That(tabs[1].Id, Is.EqualTo(TabStrip<string>.TabId("t", "b")));
        });
    }

    [Test]
    public void TabStrip_ClickRaisesActiveKeyChanged()
    {
        string? chosen = null;
        var cut = Render<TabStrip<string>>(p => p
            .Add(c => c.Items, new[] { "a", "b" })
            .Add(c => c.KeyOf, s => s)
            .Add(c => c.ActiveKey, "a")
            .Add(c => c.ActiveKeyChanged, EventCallback.Factory.Create<string>(this, k => chosen = k))
            .Add(c => c.ItemTemplate, (RenderFragment<string>)(s => b => b.AddContent(0, s))));

        cut.FindAll("[role=tab]")[1].Click();
        Assert.That(chosen, Is.EqualTo("b"));
    }

    [Test]
    public void Icon_IsDecorativeWithoutLabel_AndAnImgWithOne()
    {
        var decorative = Render<Icon>(p => p.Add(c => c.Name, "check"));
        var labelled = Render<Icon>(p => p.Add(c => c.Name, "alert").Add(c => c.Label, "Warning"));
        Assert.Multiple(() =>
        {
            Assert.That(decorative.Find("svg").GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(decorative.Find("use").GetAttribute("href"), Is.EqualTo("icons.svg#check"));
            Assert.That(labelled.Find("svg").GetAttribute("role"), Is.EqualTo("img"));
            Assert.That(labelled.Find("title").TextContent, Is.EqualTo("Warning"));
        });
    }
}

public class StateAndSessionTests
{
    [Test]
    public void SessionSnapshot_RoundTrips_AndRejectsGarbageAndOtherVersions()
    {
        var snap = new SessionSnapshot(SessionSnapshot.CurrentVersion, "rs1,NG_1:g.1A>T", 28, 10, 17, "rs1", DateTimeOffset.UtcNow);
        var back = SessionStorageService.Deserialize(SessionStorageService.Serialize(snap));

        Assert.Multiple(() =>
        {
            Assert.That(back, Is.EqualTo(snap));
            Assert.That(SessionStorageService.Deserialize(null), Is.Null);
            Assert.That(SessionStorageService.Deserialize("not json"), Is.Null);
            Assert.That(SessionStorageService.Deserialize("{\"Version\":99}"), Is.Null);
        });
    }

    [Test]
    public void Shortlist_AddReplacesSameVariantStrand_AndSendsTypedGuidesToPooling()
    {
        var state = new AppStateService();
        state.AddToShortlist(new ShortlistItem("NG_1:g.1A>T", false, "AAAA", "1", 0.5, 1));
        state.AddToShortlist(new ShortlistItem("NG_1:g.1A>T", false, "CCCC", "1", 0.9, 1));
        state.AddToShortlist(new ShortlistItem("NG_1:g.1A>T", true, "GGGG", "1", 0.7, 2));

        Assert.That(state.Shortlist, Has.Count.EqualTo(2));
        Assert.That(state.IsShortlisted("NG_1:g.1A>T", false), Is.True);

        state.SendShortlistToPooling();

        Assert.Multiple(() =>
        {
            Assert.That(state.PoolingInputMode, Is.EqualTo(PoolingInputMode.GuideList));
            Assert.That(state.PoolingParsedGuides.Select(g => g.Sequence), Is.EqualTo(new[] { "CCCC", "GGGG" }));
        });

        state.RemoveFromShortlist("NG_1:g.1A>T", true);
        Assert.That(state.Shortlist, Has.Count.EqualTo(1));
    }

    [Test]
    public void GrnaFilter_MatchesOnAllCriteria()
    {
        var g = new GRNAResult
        {
            Sequence = "ACGUACGU", SeedRegion = "ACGU", Score = 0.8, Allignments = 2, GCContent = 50,
            RnaFoldResult = new RNAFoldResult { Structure = "", Energy = 0 }
        };
        Assert.Multiple(() =>
        {
            Assert.That(new GrnaFilter().Matches(g), Is.True);
            Assert.That(new GrnaFilter { MinScore = 0.9 }.Matches(g), Is.False);
            Assert.That(new GrnaFilter { MaxAlignments = 1 }.Matches(g), Is.False);
            Assert.That(new GrnaFilter { MinGc = 55 }.Matches(g), Is.False);
            Assert.That(new GrnaFilter { MaxGc = 45 }.Matches(g), Is.False);
            Assert.That(new GrnaFilter { Contains = "cgua" }.Matches(g), Is.True);
            Assert.That(new GrnaFilter { Contains = "zzz" }.Matches(g), Is.False);
        });
    }
}

public class OffTargetTests
{
    [TestCase("ACGTACGTACGTACGTACGTACGTACGT")]
    [TestCase("TTTTGGGGCCCCAAAATTGGCCAA")]
    public void WindowFromSpacer_InvertsTheSpacerDerivation(string window)
    {
        // Same derivation as SpacerFinder.getOrderedgRna: complement, reverse, T to U.
        var spacer = new string(gRNA.Sequence.complementary(window).Reverse().ToArray()).Replace('T', 'U');
        Assert.That(GrnaService.WindowFromSpacer(spacer), Is.EqualTo(window));
    }

    [Test]
    public void ParseBowtieAlignments_ReadsMismatchColumnFromRealOutput()
    {
        // Captured from the real image: bowtie-align-s 1.3.1 against the GRCh38 .bt2 index.
        var t = ((char)9).ToString();
        var nl = ((char)10).ToString();
        var output =
            "0" + t + "+" + t + "chr22" + t + "34065972" + t + "ACGTACGTACGTACGTACGTACGTACGT" + t + "IIII" + t + "0" + t + nl +
            "0" + t + "-" + t + "chr22" + t + "34065964" + t + "ACGTACGTACGTACGTACGTACGTACGT" + t + "IIII" + t + "1" + t + "21:A>G,25:A>G" + nl;
        var parsed = gRNA.Parsing.parseBowtieAlignments(output).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Select(p => p.MismatchCount), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(parsed[1].MismatchDetail, Is.EqualTo("21:A>G,25:A>G"));
        });
    }
}
