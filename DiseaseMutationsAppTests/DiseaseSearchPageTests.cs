using Bunit;
using DiseaseMutationsApp.Pages;
using DiseaseMutationsApp.Services;
using gRNA;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FSharp.Core;

namespace DiseaseMutationsAppTests;

/// <summary>Configurable stand-in for Monarch and ClinVar: tests set what each call returns and read what was asked.</summary>
internal sealed class TestDiseaseSource : IDiseaseVariantSource
{
    public static readonly DiseaseMatchDto Sickle = new("MONDO:0011382", "sickle cell disease", new[] { "603903" }, Array.Empty<string>());
    public static readonly DiseaseMatchDto Group = new("MONDO:0019391", "Fanconi anemia", Array.Empty<string>(), new[] { "PS227650" });

    public List<string> FindCalls { get; } = new();
    public List<(DiseaseMatchDto Disease, bool Subtypes, DiseaseSignificance Min)> VariantCalls { get; } = new();

    public Func<string, CancellationToken, Task<IReadOnlyList<DiseaseMatchDto>>> OnFind { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<DiseaseMatchDto>>(new[] { Sickle, Group });

    public Func<DiseaseMatchDto, IProgress<DiseaseProgressDto>?, CancellationToken, Task<DiseaseSearchResultDto>> OnVariants { get; set; } =
        (_, _, _) => Task.FromResult(Result(Variants(8)));

    public Task<IReadOnlyList<DiseaseMatchDto>> FindDiseasesAsync(string text, CancellationToken cancellationToken = default)
    {
        FindCalls.Add(text);
        return OnFind(text, cancellationToken);
    }

    public Task<DiseaseSearchResultDto> FindVariantsAsync(DiseaseMatchDto disease, bool includeSubtypes, DiseaseSignificance minimum,
        IProgress<DiseaseProgressDto>? progress = null, CancellationToken cancellationToken = default)
    {
        VariantCalls.Add((disease, includeSubtypes, minimum));
        return OnVariants(disease, progress, cancellationToken);
    }

    /// <summary>Every seventh variant has no rsID but a genomic HGVS; the last one has neither.</summary>
    public static List<DiseaseVariantDto> Variants(int count, bool lastHasNothing = false) =>
        Enumerable.Range(1, count).Select(i =>
        {
            var noRs = i % 7 == 0;
            var nothing = lastHasNothing && i == count;
            return new DiseaseVariantDto($"{1000 + i}", $"NM_000001.1(GENE{i % 3}):c.{i}A>G", $"GENE{i % 3}",
                noRs || nothing ? null : $"rs{500 + i}", nothing ? null : $"NC_000012.12:g.{102000000 + i}A>G",
                i % 2 == 0 ? "Pathogenic" : "Likely pathogenic", i % 2 == 0 ? "reviewed by expert panel" : "criteria provided, single submitter",
                i % 2 == 0 ? 3 : 1, new[] { "A condition" });
        }).ToList();

    public static DiseaseSearchResultDto Result(IReadOnlyList<DiseaseVariantDto> variants, bool truncated = false, int total = -1,
        string[]? omim = null, int subtypesChecked = 0, int subtypesTotal = 0) =>
        new(omim ?? new[] { "603903" }, subtypesChecked, subtypesTotal, total < 0 ? variants.Count : total, truncated, variants);
}

/// <summary>The Disease variants page and the hand-off to the Diana page, with a fresh context (and state) per test.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class DiseaseSearchPageTests : BunitContext
{
    private readonly TestDiseaseSource _source = new();
    private readonly AppStateService _state = new();

    public DiseaseSearchPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDiseaseVariantSource>(_source);
        Services.AddSingleton(_state);
    }

    private IRenderedComponent<DiseaseSearch> Open() => Render<DiseaseSearch>();

    private static void Search(IRenderedComponent<DiseaseSearch> cut, string text = "sickle")
    {
        cut.Find("#disease-query").Input(text);
        cut.Find("form").Submit();
    }

    private static void FindVariants(IRenderedComponent<DiseaseSearch> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Contains("Find variants")).Click();

    /// <summary>Searches, picks the first disease and reads its variants.</summary>
    private IRenderedComponent<DiseaseSearch> OpenWithResults(Action? configure = null)
    {
        configure?.Invoke();
        var cut = Open();
        Search(cut);
        FindVariants(cut);
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Is.Not.Empty));
        return cut;
    }

    private static IEnumerable<AngleSharp.Dom.IElement> Boxes(IRenderedComponent<DiseaseSearch> cut) => cut.FindAll("tbody input[type=checkbox]");

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<DiseaseSearch> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text));

    // ---- step 1: find the disease ----------------------------------------------------------------------

    [Test]
    public void TheFormIsLabelled_HasOneH1_AndSearchNeedsText()
    {
        var cut = Open();

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("h1"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("h1").TextContent, Is.EqualTo("Disease variants"));
            Assert.That(cut.Find("label[for=disease-query]").TextContent, Does.Contain("Disease name, OMIM number or MONDO id"));
            Assert.That(Button(cut, "Find diseases").HasAttribute("disabled"), Is.True, "no text, no search");
        });

        cut.Find("#disease-query").Input("pku");
        Assert.That(Button(cut, "Find diseases").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void NothingIsSearchedUntilTheUserSubmits()
    {
        var cut = Open();
        cut.Find("#disease-query").Input("sickle cell");

        Assert.That(_source.FindCalls, Is.Empty);

        cut.Find("form").Submit();

        Assert.That(_source.FindCalls, Is.EqualTo(new[] { "sickle cell" }));
    }

    [Test]
    public void Matches_AreARadioGroup_WithTheirOmimNumbersOrSeries()
    {
        var cut = Open();
        Search(cut);

        var radios = cut.FindAll("input[type=radio][name=disease-match]");
        Assert.Multiple(() =>
        {
            Assert.That(radios, Has.Count.EqualTo(2));
            Assert.That(radios[0].HasAttribute("checked"), Is.True, "the first match is preselected");
            Assert.That(cut.Markup, Does.Contain("OMIM 603903"));
            Assert.That(cut.Markup, Does.Contain("phenotypic series PS227650"));
            Assert.That(cut.Find("fieldset legend").TextContent, Does.Contain("2 diseases found"));
        });
    }

    [Test]
    public void NoMatch_SaysSo_AndOffersAnotherRoute()
    {
        _source.OnFind = (_, _) => Task.FromResult<IReadOnlyList<DiseaseMatchDto>>(Array.Empty<DiseaseMatchDto>());
        var cut = Open();
        Search(cut, "zzzzqq");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("No disease matched"));
            Assert.That(cut.Markup, Does.Contain("zzzzqq"));
            Assert.That(cut.FindAll("input[type=radio]"), Is.Empty);
        });
    }

    [Test]
    public void ABadQuery_ShowsTheMessage_WithoutARetryButton()
    {
        _source.OnFind = (_, _) => throw new GrnaInputException("'22' is not an OMIM number.", FSharpOption<Exception>.None);
        var cut = Open();
        Search(cut, "22");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".alert-danger[role=alert]").TextContent, Does.Contain("'22' is not an OMIM number."));
            Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("Try again")), Is.False);
        });
    }

    [Test]
    public void AnUpstreamFailure_ShowsTheMessage_AndTryAgainRetries()
    {
        var calls = 0;
        _source.OnFind = (_, _) => ++calls == 1
            ? throw new GrnaUpstreamException("Monarch returned HTTP 503.", FSharpOption<Exception>.None)
            : Task.FromResult<IReadOnlyList<DiseaseMatchDto>>(new[] { TestDiseaseSource.Sickle });
        var cut = Open();
        Search(cut);

        Assert.That(cut.Find(".alert-danger").TextContent, Does.Contain("Monarch returned HTTP 503."));

        Button(cut, "Try again").Click();

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".alert-danger"), Is.Empty);
            Assert.That(cut.FindAll("input[type=radio]"), Has.Count.EqualTo(1));
            Assert.That(_source.FindCalls, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ASearchCanBeCancelled()
    {
        var gate = new TaskCompletionSource();
        _source.OnFind = async (_, ct) =>
        {
            using var reg = ct.Register(() => gate.TrySetCanceled());
            await gate.Task;
            return Array.Empty<DiseaseMatchDto>();
        };
        var cut = Open();
        Search(cut);

        Assert.That(cut.Find("#disease-status").TextContent, Does.Contain("Looking up diseases"));
        Button(cut, "Cancel").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find("#disease-status").TextContent, Does.Contain("Search cancelled.")));
        Assert.That(Button(cut, "Find diseases").HasAttribute("disabled"), Is.False, "the page is usable again");
    }

    // ---- step 2: choose and search ---------------------------------------------------------------------

    [Test]
    public void AGroupDisease_RequiresItsSubtypes_AndALeafDiseaseDoesNot()
    {
        var cut = Open();
        Search(cut);

        var box = cut.Find("#include-subtypes");
        Assert.Multiple(() =>
        {
            Assert.That(box.HasAttribute("disabled"), Is.False, "sickle cell disease has its own OMIM number");
            Assert.That(box.HasAttribute("checked"), Is.False);
        });

        cut.FindAll("input[type=radio]")[1].Change(true);   // Fanconi anemia: a phenotypic series

        box = cut.Find("#include-subtypes");
        Assert.Multiple(() =>
        {
            Assert.That(box.HasAttribute("disabled"), Is.True);
            Assert.That(box.HasAttribute("checked"), Is.True);
            Assert.That(cut.Markup, Does.Contain("Required: this disease is a group"));
        });
    }

    [Test]
    public void FindVariants_PassesTheChosenDisease_Subtypes_AndClassification()
    {
        var cut = Open();
        Search(cut);
        cut.Find("#include-subtypes").Change(true);
        cut.Find("#min-significance").Change(DiseaseSignificance.PathogenicOnly.ToString());

        FindVariants(cut);

        var call = _source.VariantCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Disease.Id, Is.EqualTo("MONDO:0011382"));
            Assert.That(call.Subtypes, Is.True);
            Assert.That(call.Min, Is.EqualTo(DiseaseSignificance.PathogenicOnly));
        });
    }

    [Test]
    public void FindVariants_ForAGroup_AlwaysAsksForSubtypes()
    {
        var cut = Open();
        Search(cut);
        cut.FindAll("input[type=radio]")[1].Change(true);

        FindVariants(cut);

        Assert.That(_source.VariantCalls.Single().Subtypes, Is.True);
    }

    [Test]
    public void ProgressIsAnnounced_AndTheSearchCanBeCancelled()
    {
        IProgress<DiseaseProgressDto>? progress = null;
        var gate = new TaskCompletionSource<DiseaseSearchResultDto>();
        _source.OnVariants = (_, p, ct) =>
        {
            progress = p;
            ct.Register(() => gate.TrySetCanceled());
            return gate.Task;
        };
        var cut = Open();
        Search(cut);
        FindVariants(cut);

        progress!.Report(new DiseaseProgressDto("Reading variant details", 40, 120));

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Find("#disease-status").TextContent, Does.Contain("Reading variant details").And.Contain("40 of 120"));
            Assert.That(cut.Find("#disease-status progress").GetAttribute("max"), Is.EqualTo("120"));
            Assert.That(cut.Find("#disease-status progress").GetAttribute("value"), Is.EqualTo("40"));
        });

        Button(cut, "Cancel").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Find("#disease-status").TextContent, Does.Contain("Search cancelled.")));
        Assert.That(cut.FindAll("tbody tr"), Is.Empty);
    }

    [Test]
    public void AVariantSearchFailure_OffersTryAgain()
    {
        var calls = 0;
        _source.OnVariants = (_, _, _) => ++calls == 1
            ? throw new GrnaUpstreamException("NCBI esearch returned HTTP 503.", FSharpOption<Exception>.None)
            : Task.FromResult(TestDiseaseSource.Result(TestDiseaseSource.Variants(3)));
        var cut = Open();
        Search(cut);
        FindVariants(cut);

        Assert.That(cut.Find(".alert-danger").TextContent, Does.Contain("NCBI esearch returned HTTP 503."));
        Button(cut, "Try again").Click();

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(3));
    }

    // ---- step 3: results -------------------------------------------------------------------------------

    [Test]
    public void Results_ShowTheSummary_TheTable_AndTheCounts()
    {
        var cut = OpenWithResults();

        var text = System.Text.RegularExpressions.Regex.Replace(cut.Find("p.helper-note[role=status]").TextContent, @"\s+", " ");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("table caption").TextContent, Does.Contain("Variants of sickle cell disease"));
            Assert.That(cut.FindAll("thead th[scope=col]"), Has.Count.EqualTo(7));
            Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(8));
            Assert.That(text, Does.Contain("8 ClinVar records classified pathogenic or likely pathogenic for OMIM 603903"));
            Assert.That(text, Does.Contain("7 of the 8 listed have an rsID (7 distinct"));
            Assert.That(text, Does.Contain("the other 1 show a genomic HGVS instead"));
            Assert.That(cut.FindAll("tbody tr")[6].TextContent, Does.Contain("NC_000012.12:g.102000007A>G"), "the row without an rsID shows its HGVS");
            Assert.That(cut.Find("a[href='https://www.ncbi.nlm.nih.gov/snp/rs501']"), Is.Not.Null);
            Assert.That(cut.Find("a[href='https://www.ncbi.nlm.nih.gov/clinvar/variation/1001/']"), Is.Not.Null);
        });
    }

    [Test]
    public void ARowWithNeitherAnRsIdNorAnHgvs_CannotBeSelectedOrSent()
    {
        var cut = OpenWithResults(() =>
            _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(TestDiseaseSource.Variants(7, lastHasNothing: true))));

        var rows = cut.FindAll("tbody tr");
        Assert.Multiple(() =>
        {
            Assert.That(rows[6].QuerySelector("input[type=checkbox]")!.HasAttribute("disabled"), Is.True, "no rsID and no HGVS: nothing to send");
            Assert.That(rows[6].QuerySelector("input[type=checkbox]")!.GetAttribute("aria-label"), Does.Contain("has no rsID or HGVS to send"));
            Assert.That(rows[6].QuerySelectorAll("button").First().HasAttribute("disabled"), Is.True);
        });
    }

    [Test]
    public void TheEmptyResult_ExplainsAndSuggestsLoosening()
    {
        _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(Array.Empty<DiseaseVariantDto>()));
        var cut = Open();
        Search(cut);
        FindVariants(cut);

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No variants at this classification")));
        Assert.That(cut.FindAll("table"), Is.Empty);
    }

    [Test]
    public void ADiseaseWithoutOmimNumbers_SaysSo()
    {
        _source.OnVariants = (_, _, _) => Task.FromResult(new DiseaseSearchResultDto(Array.Empty<string>(), 22, 22, 0, false, Array.Empty<DiseaseVariantDto>()));
        var cut = Open();
        Search(cut);
        FindVariants(cut);

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No OMIM number to search")));
        Assert.That(cut.Markup, Does.Contain("22 of its 22 subtypes"));
    }

    [Test]
    public void ATruncatedResult_WarnsThatTheListIsIncomplete()
    {
        var cut = OpenWithResults(() =>
            _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(TestDiseaseSource.Variants(10), truncated: true, total: 4543)));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".alert-warning[role=alert]").TextContent, Does.Contain("The list is incomplete").And.Contain("4543 records matched"));
            Assert.That(cut.Markup, Does.Contain("4543 ClinVar records"));
        });
    }

    [Test]
    public void SubtypesConsulted_AreReportedInTheSummary()
    {
        var cut = OpenWithResults(() =>
            _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(TestDiseaseSource.Variants(3), omim: new[] { "227645", "227646" }, subtypesChecked: 22, subtypesTotal: 22)));

        Assert.That(cut.Markup, Does.Contain("Consulted 22 of 22 subtypes"));
        Assert.That(cut.Markup, Does.Contain("OMIM 227645, 227646"));
    }

    // ---- filtering, sorting, paging --------------------------------------------------------------------

    private IRenderedComponent<DiseaseSearch> Open70() =>
        OpenWithResults(() => _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(TestDiseaseSource.Variants(70))));

    [Test]
    public void ThePageSizeAndPager_Work()
    {
        var cut = Open70();

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(25));
        Assert.That(cut.Find(".pager").TextContent, Does.Contain("Page 1 of 3"));
        Assert.That(Button(cut, "Previous").HasAttribute("disabled"), Is.True);

        Button(cut, "Next").Click();
        Button(cut, "Next").Click();

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(20));
            Assert.That(cut.Find(".pager").TextContent, Does.Contain("Page 3 of 3"));
            Assert.That(Button(cut, "Next").HasAttribute("disabled"), Is.True);
        });

        cut.Find("#variant-page-size").Change("100");

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(70));
        Assert.That(cut.FindAll(".pager"), Is.Empty);
    }

    [Test]
    public void OnlyWithAnRsId_HidesTheHgvsOnlyRows_AndClearFiltersRestoresThem()
    {
        var cut = Open70();

        cut.Find("#only-rs").Change(true);
        cut.Find("#variant-page-size").Change("100");

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(60), "every seventh of 70 has no rsID");
        Assert.That(cut.Markup, Does.Contain("filtered from 70"));

        Button(cut, "Clear filters").Click();

        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(70));
    }

    [Test]
    public void TheTextFilter_MatchesGeneRsIdAndCondition()
    {
        var cut = Open70();
        cut.Find("#variant-page-size").Change("100");

        cut.Find("#variant-filter").Input("rs510");
        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1));

        cut.Find("#variant-filter").Input("GENE2");
        Assert.That(cut.FindAll("tbody tr").All(r => r.TextContent.Contains("GENE2")), Is.True);

        cut.Find("#variant-filter").Input("a condition");
        Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(70));

        cut.Find("#variant-filter").Input("nothing like this");
        Assert.That(cut.FindAll("tbody tr"), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Showing 0 of 0 variants"));
    }

    [Test]
    public void SortingByAColumn_TogglesTheDirection_AndExposesAriaSort()
    {
        var cut = Open70();
        cut.Find("#variant-page-size").Change("100");
        var gene = () => cut.FindAll("thead th").First(h => h.TextContent.Contains("Gene"));

        Assert.That(gene().GetAttribute("aria-sort"), Is.EqualTo("none"));

        gene().QuerySelector("button")!.Click();
        Assert.Multiple(() =>
        {
            Assert.That(gene().GetAttribute("aria-sort"), Is.EqualTo("ascending"));
            Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Contain("GENE0"));
        });

        gene().QuerySelector("button")!.Click();
        Assert.Multiple(() =>
        {
            Assert.That(gene().GetAttribute("aria-sort"), Is.EqualTo("descending"));
            Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Contain("GENE2"));
        });
    }

    [Test]
    public void SortingByReview_OrdersByStars()
    {
        var cut = Open70();
        cut.Find("#variant-page-size").Change("100");

        cut.FindAll("thead th").First(h => h.TextContent.Contains("Review")).QuerySelector("button")!.Click();
        var first = cut.FindAll("tbody tr")[0].TextContent;
        cut.FindAll("thead th").First(h => h.TextContent.Contains("Review")).QuerySelector("button")!.Click();
        var last = cut.FindAll("tbody tr")[0].TextContent;

        Assert.That(first, Does.Contain("1 of 4 stars"));
        Assert.That(last, Does.Contain("3 of 4 stars"));
    }

    // ---- selection and hand-off to Diana ---------------------------------------------------------------

    [Test]
    public void NoSelection_NoActionBar()
    {
        var cut = OpenWithResults();
        Assert.That(cut.FindAll(".selection-bar"), Is.Empty);
    }

    [Test]
    public void SelectingRows_ShowsTheCount_AndClearEmptiesIt()
    {
        var cut = OpenWithResults();

        Boxes(cut).ElementAt(0).Change(true);
        Boxes(cut).ElementAt(1).Change(true);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".selection-bar").GetAttribute("role"), Is.EqualTo("region"));
            Assert.That(cut.Find(".selection-summary").TextContent, Does.Contain("2").And.Contain("selected"));
            Assert.That(cut.FindAll("tbody tr.row-selected"), Has.Count.EqualTo(2));
        });

        Button(cut, "Clear").Click();

        Assert.That(cut.FindAll(".selection-bar"), Is.Empty);
        Assert.That(cut.FindAll("tbody tr.row-selected"), Is.Empty);
    }

    [Test]
    public void SelectAllMatching_RespectsTheFilter_AcrossPages()
    {
        var cut = Open70();

        cut.Find("#only-rs").Change(true);
        Button(cut, "Select all 60 matching").Click();

        Assert.That(cut.Find(".selection-summary").TextContent, Does.Contain("60"));
        Assert.That(cut.FindAll("tbody tr.row-selected"), Has.Count.EqualTo(25), "the visible page");
    }

    [Test]
    public void TwoRowsSharingAnRsId_CountAsOneInput()
    {
        var shared = new List<DiseaseVariantDto>
        {
            new("1", "haplotype A", "HBB", "rs334", null, "Pathogenic", "", 0, Array.Empty<string>()),
            new("2", "haplotype B", "HBB", "rs334", null, "Pathogenic", "", 0, Array.Empty<string>()),
            new("3", "other", "HBB", "rs1", null, "Pathogenic", "", 0, Array.Empty<string>())
        };
        var cut = OpenWithResults(() => _source.OnVariants = (_, _, _) => Task.FromResult(TestDiseaseSource.Result(shared)));

        Button(cut, "Select all 3 matching").Click();

        Assert.That(cut.Find(".selection-summary strong").TextContent, Is.EqualTo("2"));
        Assert.That(cut.Markup, Does.Contain("(2 distinct"));
    }

    [Test]
    public void SendReplace_PutsTheSelectionInTheDianaBox_AndOpensDiana()
    {
        _state.IndexHgvsInput = "rs1, NC_000017.11:g.7674220C>T";
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);   // rs501
        Boxes(cut).ElementAt(2).Change(true);   // rs503

        Button(cut, "Send to Diana (replace)").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(_state.IndexHgvsInput, Is.EqualTo("rs501, rs503"));
            Assert.That(_state.IndexIncomingNotice, Is.Not.Null);
            Assert.That(_state.IndexIncomingNotice!.Added, Is.EqualTo(2));
            Assert.That(nav.Uri, Is.EqualTo(nav.BaseUri), "now on the Diana page");
        });
    }

    [Test]
    public void SendAdd_KeepsWhatWasThere_AndDropsDuplicates()
    {
        _state.IndexHgvsInput = "rs501, NC_000017.11:g.7674220C>T";
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);   // rs501: already there
        Boxes(cut).ElementAt(1).Change(true);   // rs502

        Button(cut, "Add to Diana inputs").Click();

        Assert.Multiple(() =>
        {
            Assert.That(_state.IndexHgvsInput, Is.EqualTo("rs501, NC_000017.11:g.7674220C>T, rs502"));
            Assert.That(_state.IndexIncomingNotice!.Added, Is.EqualTo(1));
            Assert.That(_state.IndexIncomingNotice.AlreadyPresent, Is.EqualTo(1));
        });
    }

    [Test]
    public void TheRowButton_AddsOneVariant_WithoutLosingTheBox()
    {
        _state.IndexHgvsInput = "rs9";
        var cut = OpenWithResults();

        cut.Find("button[aria-label='Send rs502 to Diana']").Click();

        Assert.That(_state.IndexHgvsInput, Is.EqualTo("rs9, rs502"));
    }

    [Test]
    public void ARowWithoutAnRsId_SendsItsHgvs()
    {
        var cut = OpenWithResults();

        cut.Find("button[aria-label='Send NC_000012.12:g.102000007A>G to Diana']").Click();

        Assert.That(_state.IndexHgvsInput, Is.EqualTo("NC_000012.12:g.102000007A>G"));
    }

    [Test]
    public void MoreThanTheRunLimit_DisablesSending_AndExplainsWhy()
    {
        var cut = Open70();

        cut.Find("#only-rs").Change(true);
        Button(cut, "Select all 60 matching").Click();

        var message = cut.Find(".selection-bar [role=alert]").TextContent;
        Assert.Multiple(() =>
        {
            Assert.That(Button(cut, "Send to Diana (replace)").HasAttribute("disabled"), Is.True);
            Assert.That(Button(cut, "Add to Diana inputs").HasAttribute("disabled"), Is.True);
            Assert.That(message, Does.Contain("at most 50").And.Contain("60 are selected"));
            Assert.That(Button(cut, "Copy list").HasAttribute("disabled"), Is.False, "copying and downloading have no limit");
            Assert.That(Button(cut, "Download list").HasAttribute("disabled"), Is.False);
        });
    }

    [Test]
    public void Adding_ToAFullBox_IsBlocked_ButReplacingIsNot()
    {
        _state.IndexHgvsInput = string.Join(", ", Enumerable.Range(1, 49).Select(i => $"rs{i}"));
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);
        Boxes(cut).ElementAt(1).Change(true);

        Assert.Multiple(() =>
        {
            Assert.That(Button(cut, "Add to Diana inputs").HasAttribute("disabled"), Is.True, "49 + 2 would be 51");
            Assert.That(Button(cut, "Send to Diana (replace)").HasAttribute("disabled"), Is.False);
            Assert.That(cut.Find(".selection-bar [role=alert]").TextContent, Does.Contain("would hold 51 inputs"));
        });
    }

    [Test]
    public void CopyList_CopiesTheInputsOnePerLine()
    {
        JSInterop.Setup<bool>("grnaCopy", _ => true).SetResult(true);
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);
        Boxes(cut).ElementAt(1).Change(true);

        Button(cut, "Copy list").Click();

        Assert.Multiple(() =>
        {
            Assert.That(JSInterop.Invocations["grnaCopy"].Single().Arguments[0], Is.EqualTo("rs501\nrs502"));
            Assert.That(cut.Find(".selection-bar [role=status]").TextContent, Does.Contain("Copied 2"));
        });
    }

    [Test]
    public void DownloadList_SavesATextFileOfTheSelection()
    {
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);
        Boxes(cut).ElementAt(1).Change(true);

        Button(cut, "Download list").Click();

        var call = JSInterop.Invocations["downloadFile"].Single();
        Assert.Multiple(() =>
        {
            Assert.That((string)call.Arguments[0]!, Does.StartWith("Disease_sickle_cell_disease_selected_").And.EndWith(".txt"));
            Assert.That(call.Arguments[1], Is.EqualTo("text/plain;charset=utf-8"));
            Assert.That(call.Arguments[2], Is.EqualTo("rs501\nrs502\n"));
        });
    }

    [Test]
    public void DownloadAllRsIds_AndTheTable_AreAvailableWithoutASelection()
    {
        var cut = OpenWithResults();

        Button(cut, "All rsIDs").Click();
        Button(cut, "Table (.csv)").Click();

        var calls = JSInterop.Invocations["downloadFile"].ToList();
        Assert.Multiple(() =>
        {
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(((string)calls[0].Arguments[2]!).Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(7), "the HGVS-only row is not an rsID");
            Assert.That((string)calls[1].Arguments[0]!, Does.EndWith(".csv"));
            Assert.That((string)calls[1].Arguments[2]!, Does.StartWith("rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions"));
        });
    }

    // ---- state kept across visits ----------------------------------------------------------------------

    [Test]
    public void LeavingAndComingBack_KeepsTheResultAndTheSelection()
    {
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);
        cut.Find("#variant-filter").Input("GENE");
        cut.Dispose();

        var again = Render<DiseaseSearch>();

        Assert.Multiple(() =>
        {
            Assert.That(again.FindAll("tbody tr"), Has.Count.EqualTo(8));
            Assert.That(again.Find(".selection-summary").TextContent, Does.Contain("1"));
            Assert.That(again.Find("#variant-filter").GetAttribute("value"), Is.EqualTo("GENE"));
            Assert.That(again.Find("#disease-query").GetAttribute("value"), Is.EqualTo("sickle"));
            Assert.That(_source.FindCalls, Has.Count.EqualTo(1), "nothing is searched again");
        });
    }

    [Test]
    public void ANewDiseaseSearch_DropsTheOldResultAndSelection()
    {
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);

        Search(cut, "another");

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("table"), Is.Empty);
            Assert.That(cut.FindAll(".selection-bar"), Is.Empty);
        });
    }

    // ---- accessibility markup --------------------------------------------------------------------------

    [Test]
    public void TheControls_HaveAccessibleNames()
    {
        var cut = OpenWithResults();
        Boxes(cut).ElementAt(0).Change(true);

        var unlabeled = cut.FindAll("input, select").Where(e =>
        {
            var id = e.GetAttribute("id");
            return string.IsNullOrEmpty(e.GetAttribute("aria-label")) && (id is null || cut.FindAll($"label[for='{id}']").Count == 0);
        }).ToList();
        var nameless = cut.FindAll("button").Where(b => string.IsNullOrWhiteSpace(b.TextContent) && string.IsNullOrEmpty(b.GetAttribute("aria-label"))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(unlabeled.Select(e => e.OuterHtml), Is.Empty, "every field has a label or aria-label");
            Assert.That(nameless.Select(b => b.OuterHtml), Is.Empty, "every icon button has an aria-label");
            Assert.That(cut.Find(".grna-table-wrap").GetAttribute("tabindex"), Is.EqualTo("0"), "a scroll region is keyboard reachable");
            Assert.That(cut.Find(".grna-table-wrap").GetAttribute("role"), Is.EqualTo("region"));
            Assert.That(cut.FindAll("a[target=_blank]").All(a => a.GetAttribute("rel")!.Contains("noopener")), Is.True);
            Assert.That(cut.Find("#disease-status").GetAttribute("aria-live"), Is.EqualTo("polite"));
            Assert.That(cut.Find(".selection-summary").GetAttribute("aria-live"), Is.EqualTo("polite"));
        });
    }

    [Test]
    public void ExternalLinks_SayTheyOpenAnotherSite()
    {
        var cut = OpenWithResults();

        var link = cut.Find("a[href='https://www.ncbi.nlm.nih.gov/snp/rs501']");

        Assert.That(link.TextContent, Does.Contain("opens dbSNP"));
    }
}

/// <summary>What the hand-off does to the state, and how the Diana page shows it.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class SendToBuilderTests : BunitContext
{
    [TestCase(false, "rs1, rs2", "rs3,rs4", "rs3, rs4", 2, 0, 2)]
    [TestCase(true, "rs1, rs2", "rs3,rs4", "rs1, rs2, rs3, rs4", 2, 0, 4)]
    [TestCase(true, "rs1, rs2", "RS1,rs3", "rs1, rs2, rs3", 1, 1, 3)]
    [TestCase(true, "", "rs1", "rs1", 1, 0, 1)]
    [TestCase(true, null, "rs1", "rs1", 1, 0, 1)]
    [TestCase(false, "rs1", "rs2,rs2,Rs2", "rs2", 1, 0, 1)]
    public void MergesDeduplicatesAndCounts(bool append, string? existing, string incoming, string expectedBox, int added, int already, int total)
    {
        var state = new AppStateService { IndexHgvsInput = existing };

        var notice = state.SendToBuilder(incoming.Split(','), append);

        Assert.Multiple(() =>
        {
            Assert.That(state.IndexHgvsInput, Is.EqualTo(expectedBox));
            Assert.That(notice.Added, Is.EqualTo(added));
            Assert.That(notice.AlreadyPresent, Is.EqualTo(already));
            Assert.That(notice.Total, Is.EqualTo(total));
            Assert.That(state.IndexIncomingNotice, Is.EqualTo(notice));
        });
    }

    [Test]
    public void SendingNothing_ReplacesWithAnEmptyBox_AndNotifiesListeners()
    {
        var state = new AppStateService { IndexHgvsInput = "rs1" };
        var changes = 0;
        state.OnStateChanged += () => changes++;

        state.SendToBuilder(Array.Empty<string>(), append: false);

        Assert.That(state.IndexHgvsInput, Is.EqualTo(""));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void TheNoticeReadsNaturally()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new IncomingNotice("Disease variants", 3, 0, 3, false).Message, Is.EqualTo("Disease variants: 3 inputs put in the box."));
            Assert.That(new IncomingNotice("Disease variants", 1, 2, 5, true).Message, Is.EqualTo("Disease variants: 1 input added (2 already there), 5 in total."));
        });
    }

    [Test]
    public void ClearingTheDianaState_AlsoDropsTheNoticeAndTheDiseaseSearch()
    {
        var state = new AppStateService();
        state.SendToBuilder(new[] { "rs1" }, false);
        state.DiseaseSearch.Query = "x";

        state.ClearIndexState();
        Assert.That(state.IndexIncomingNotice, Is.Null);

        state.SendToBuilder(new[] { "rs1" }, false);
        state.ClearAll();
        Assert.That(state.IndexIncomingNotice, Is.Null);
        Assert.That(state.DiseaseSearch.Query, Is.EqualTo(""));
    }

    private IRenderedComponent<DiseaseMutationsApp.Pages.Index> RenderDiana(AppStateService state)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(state);
        Services.AddSingleton(new GrnaService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService()));
        Services.AddSingleton(new AnalysisRunner(new NoAnalysis(), state, Microsoft.Extensions.Options.Options.Create(new AnalysisOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalysisRunner>.Instance));
        Services.AddSingleton<SessionStorageService>();
        return Render<DiseaseMutationsApp.Pages.Index>();
    }

    private sealed class NoAnalysis : IGrnaAnalysis
    {
        public Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? GetNcbiNuccoreUrl(string hgvs) => null;
    }

    [Test]
    public void TheDianaPage_ShowsTheSentInputs_AndABanner_WithoutRunningAnything()
    {
        var state = new AppStateService();
        state.SendToBuilder(new[] { "rs334", "rs1" }, append: false);

        var cut = RenderDiana(state);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("#hgvs").GetAttribute("value"), Is.EqualTo("rs334, rs1"));
            Assert.That(cut.Find("#incoming-banner").TextContent, Does.Contain("Disease variants: 2 inputs put in the box.").And.Contain("press Run analysis"));
            Assert.That(cut.Find("#incoming-banner").GetAttribute("role"), Is.EqualTo("status"));
            Assert.That(cut.FindAll("[role=tab]"), Is.Empty, "arriving never starts a run");
            Assert.That(state.IndexIncomingNotice, Is.Null, "the notice is shown once");
        });
    }

    [Test]
    public void TheDianaPage_ShowsNoBanner_WhenNothingWasSent()
    {
        var state = new AppStateService { IndexHgvsInput = "rs334" };

        var cut = RenderDiana(state);

        Assert.That(cut.FindAll("#incoming-banner"), Is.Empty);
    }

    [Test]
    public void StartingARun_RemovesTheBanner()
    {
        var state = new AppStateService();
        state.SendToBuilder(new[] { "rs334" }, false);
        var cut = RenderDiana(state);

        cut.FindAll("button").First(b => b.TextContent.Contains("Run analysis")).Click();

        Assert.That(cut.FindAll("#incoming-banner"), Is.Empty);
    }
}
