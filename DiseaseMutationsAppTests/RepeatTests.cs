using System.Diagnostics;
using DiseaseMutationsApp.Services;
using gRNA;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiseaseMutationsAppTests;

/// <summary>The direct-repeat choice for screening: normalising what is typed, how the choice travels, and what it changes.</summary>
public class RepeatTests
{
    private const string Spacer = "ACGUACGUACGUACGUACGUACGUACGU";

    // ===== normaliseRepeat =====

    [Test]
    public void Normalise_UppercasesTurnsTIntoUAndDropsBlanks()
        => Assert.That(SpacerFinder.normaliseRepeat(" gatt tagac\n"), Is.EqualTo("GAUUUAGAC"));

    [Test]
    public void Normalise_EmptyStaysEmpty()
        => Assert.That(SpacerFinder.normaliseRepeat("  "), Is.EqualTo(""));

    [TestCase("GAUXUU")]
    [TestCase("GAU-UU")]
    [TestCase("GAU1UU")]
    public void Normalise_RejectsLettersThatAreNotNucleotides(string typed)
        => Assert.Throws<GrnaInputException>(() => SpacerFinder.normaliseRepeat(typed));

    [Test]
    public void Normalise_RejectsAnythingLongerThanTheLimit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.normaliseRepeat(new string('A', SpacerFinder.MaxRepeatLength)), Has.Length.EqualTo(SpacerFinder.MaxRepeatLength));
            Assert.Throws<GrnaInputException>(() => SpacerFinder.normaliseRepeat(new string('A', SpacerFinder.MaxRepeatLength + 1)));
        });
    }

    // ===== RepeatChoice =====

    [Test]
    public void Resolve_DefaultIsNull_NoneIsEmpty_CustomIsNormalised()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatChoice.Resolve(RepeatMode.Default, "ignored"), Is.Null);
            Assert.That(RepeatChoice.Resolve(RepeatMode.None, "ignored"), Is.EqualTo(""));
            Assert.That(RepeatChoice.Resolve(RepeatMode.Custom, "gauu uuc"), Is.EqualTo("GAUUUUC"));
        });
    }

    [TestCase(null)]
    [TestCase("   ")]
    [TestCase("GAUQ")]
    public void Validate_CustomNeedsAUsableSequence(string? typed)
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatChoice.Validate(RepeatMode.Custom, typed), Is.Not.Null);
            Assert.That(RepeatChoice.Validate(RepeatMode.Default, typed), Is.Null, "the other modes ignore the text box");
            Assert.That(RepeatChoice.Validate(RepeatMode.None, typed), Is.Null);
        });
    }

    [Test]
    public void Token_RoundTripsEveryChoice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatChoice.FromToken(RepeatChoice.ToToken(RepeatMode.Default, null)), Is.EqualTo((RepeatMode.Default, (string?)null)));
            Assert.That(RepeatChoice.FromToken(RepeatChoice.ToToken(RepeatMode.None, null)), Is.EqualTo((RepeatMode.None, (string?)null)));
            Assert.That(RepeatChoice.FromToken(RepeatChoice.ToToken(RepeatMode.Custom, "gauuuc")), Is.EqualTo((RepeatMode.Custom, (string?)"GAUUUC")));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a sequence!")]
    public void FromToken_FallsBackToTheDefault_NeverThrows(string? token)
        => Assert.That(RepeatChoice.FromToken(token), Is.EqualTo((RepeatMode.Default, (string?)null)));

    [Test]
    public void ToToken_ABrokenCustomRepeatIsNotWrittenToFilesAsAGarbageSequence()
        => Assert.That(RepeatChoice.ToToken(RepeatMode.Custom, "GAUQ"), Is.EqualTo(RepeatChoice.DefaultToken));

    // ===== Report provenance =====

    [Test]
    public void Provenance_RecordsTheRepeatAndReadsItBack()
    {
        var withCustom = ReportSchema.provenanceWithRepeat("GAUUUC", 28, 10, 17);
        var withNone = ReportSchema.provenanceWithRepeat("", 28, 10, 17);
        var byDefault = GrnaCsvSchema.ProvenanceLine(28, 10, 17);
        Assert.Multiple(() =>
        {
            Assert.That(ReportSchema.tryParseRepeat(withCustom).Value, Is.EqualTo("GAUUUC"));
            Assert.That(ReportSchema.tryParseRepeat(withNone).Value, Is.EqualTo(""));
            Assert.That(ReportSchema.tryParseRepeat(byDefault).Value, Is.EqualTo(SpacerFinder.scaffold));
            Assert.That(ReportSchema.tryParseProvenance(withCustom).Value, Is.EqualTo(Tuple.Create(28, 10, 17)), "spacer and seed still parse");
            Assert.That(GrnaCsvSchema.ProvenanceLine(28, 10, 17, repeat: ""), Does.Contain("repeat=none"));
        });
    }

    [Test]
    public void Provenance_OldReportsWithoutARepeatWereMadeWithTheDefaultOne()
    {
        const string old = "# Diana report; spacer=28; seed=10-17; generated=2026-10-01T00:00:00Z; gc-score-range=40-60; scaffold=36 nt";
        Assert.Multiple(() =>
        {
            Assert.That(ReportSchema.tryParseRepeat(old).Value, Is.EqualTo(SpacerFinder.scaffold));
            Assert.That(Microsoft.FSharp.Core.FSharpOption<string>.get_IsNone(ReportSchema.tryParseRepeat("rs1,NG_1.1:g.1A>T")), Is.True, "not a provenance line");
        });
    }

    // ===== The run carries the choice to the analysis =====

    private sealed class RecordingAnalysis : IGrnaAnalysis
    {
        public readonly List<(string? Repeat, int SnpPos, int MismatchPos)> Calls = new();

        public Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the runner must use the rule-aware call");

        public Task<ResultFromHGVS> GetBestgRNAFromHgvsWithRule(string hgvs, int window, int seedStart, int seedEnd, SpacerFinder.SpecialRuleOptions rule, bool complement = false, CancellationToken cancellationToken = default, string? repeat = null)
        {
            lock (Calls) Calls.Add((repeat, rule.SnpPos, rule.MismatchPos));
            return Task.FromResult(new ResultFromHGVS { gRNA = new(), OriginalGRNA = new(), MutatedSequence = "ACGT", OriginalSequence = "ACGA" });
        }

        public Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public string? GetNcbiNuccoreUrl(string hgvs) => null;
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("GAUUUC")]
    public async Task Runner_PassesTheRepeatOfTheRunToEveryVariant(string? repeat)
    {
        var fake = new RecordingAnalysis();
        var runner = new AnalysisRunner(fake, new AppStateService(), Options.Create(new AnalysisOptions()), NullLogger<AnalysisRunner>.Instance);

        Assert.That(runner.TryStart("NG_1.1:g.1A>T, NG_1.1:g.2A>T", new RunParameters(28, 10, 17, 2, 5, repeat)), Is.True);
        await runner.WaitForRunAsync();

        Assert.That(fake.Calls, Is.EqualTo(new[] { (repeat, 2, 5), (repeat, 2, 5) }), "the repeat travels together with the substitution rule");
    }

    // ===== Checks on the fold of a larger construct =====

    [Test]
    public async Task CheckWithin_MeasuresTheSpacerWhereItSitsInTheConstruct()
    {
        // No repeat: nothing to fold, so this needs no ViennaRNA. The spacer is the last four letters, all unpaired.
        var (intact, unpaired) = await SpacerFinder.checkWithin("", 0, 6, 4, "((..))....");
        Assert.Multiple(() =>
        {
            Assert.That(intact, Is.True, "no repeat, so no hairpin to break");
            Assert.That(unpaired, Is.EqualTo(1.0));
        });
        var (_, paired) = await SpacerFinder.checkWithin("", 0, 4, 4, "((..))....");
        Assert.That(paired, Is.EqualTo(0.5), "positions 4-7 are ')', ')', '.', '.'");
    }

    [Test]
    public async Task CheckWithin_ASpacerThatDoesNotFitTheStructureIsNotComputed()
    {
        var (_, unpaired) = await SpacerFinder.checkWithin("", 0, 8, 4, "((..))....");
        Assert.That(unpaired, Is.EqualTo(-1.0));
    }

    // ===== Folding with the real ViennaRNA (skipped without it) =====

    private static bool ViennaAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import RNA");
            using var p = Process.Start(psi)!;
            p.WaitForExit(20000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public async Task FoldConstructs_WithoutARepeatFoldsTheSpacerAlone_AndNeverReportsABrokenHairpin()
    {
        if (!ViennaAvailable()) Assert.Ignore("python3 with ViennaRNA is not available.");

        var folds = (await SpacerFinder.foldConstructs("", Microsoft.FSharp.Collections.ListModule.OfArray(new[] { "GCGCAAAAGCGC" }), CancellationToken.None)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(folds, Has.Length.EqualTo(1));
            Assert.That(folds[0].Fold.Structure, Has.Length.EqualTo(12), "only the spacer is folded");
            Assert.That(folds[0].Intact, Is.True);
            Assert.That(folds[0].Unpaired, Is.EqualTo(4.0 / 12).Within(1e-9));
        });
    }

    [Test]
    public async Task FoldConstructs_WithTheStandardRepeatFoldsRepeatPlusSpacer()
    {
        if (!ViennaAvailable()) Assert.Ignore("python3 with ViennaRNA is not available.");

        var folds = (await SpacerFinder.foldConstructs(SpacerFinder.scaffold, Microsoft.FSharp.Collections.ListModule.OfArray(new[] { "AAAAAAAAAAAA" }), CancellationToken.None)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(folds[0].Fold.Structure, Has.Length.EqualTo(SpacerFinder.scaffold.Length + 12));
            Assert.That(folds[0].Intact, Is.True, "a poly-A spacer leaves the repeat's hairpin alone");
        });
    }
}
