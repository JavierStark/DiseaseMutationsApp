using System;
using System.Linq;
using gRNA;

namespace DiseaseMutationsAppTests;

public class SpacerFinderTests
{
    // ===== slidingWindow =====

    [Test]
    public void SlidingWindow_ReturnsAllSubstringsOfGivenSize()
    {
        var windows = SpacerFinder.slidingWindow("ABCDE", 3).ToArray();

        Assert.That(windows, Is.EqualTo(new[] { "ABC", "BCD", "CDE" }));
    }

    [Test]
    public void SlidingWindow_InputShorterThanWindow_ReturnsEmpty()
    {
        var windows = SpacerFinder.slidingWindow("AB", 5);

        Assert.That(windows, Is.Empty);
    }

    [Test]
    public void SlidingWindow_ZeroOrNegativeWindowSize_Throws()
    {
        Assert.That(() => SpacerFinder.slidingWindow("ABCDE", 0), Throws.ArgumentException);
        Assert.That(() => SpacerFinder.slidingWindow("ABCDE", -1), Throws.ArgumentException);
    }

    // ===== calculateGCContent =====

    [Test]
    public void CalculateGCContent_EmptySequence_ReturnsZero()
    {
        Assert.That(SpacerFinder.calculateGCContent(""), Is.EqualTo(0.0));
    }

    [Test]
    public void CalculateGCContent_HalfGC_Returns50()
    {
        Assert.That(SpacerFinder.calculateGCContent("ATGC"), Is.EqualTo(50.0));
    }

    [Test]
    public void CalculateGCContent_RoundsToTwoDecimals()
    {
        // 2 of 3 bases are G/C => 66.666...% rounds to 66.67
        Assert.That(SpacerFinder.calculateGCContent("GCA"), Is.EqualTo(66.67));
    }

    // ===== calculateGCScore =====

    [Test]
    public void CalculateGCScore_WithinRange_ReturnsOne()
    {
        var score = SpacerFinder.calculateGCScore(50.0, 40.0, 60.0);
        Assert.That(score, Is.EqualTo(1.0));
    }

    [Test]
    public void CalculateGCScore_BelowRange_IsProportional()
    {
        var score = SpacerFinder.calculateGCScore(20.0, 40.0, 60.0);
        Assert.That(score, Is.EqualTo(20.0 / 40.0));
    }

    [Test]
    public void CalculateGCScore_AboveRange_IsProportional()
    {
        var score = SpacerFinder.calculateGCScore(80.0, 40.0, 60.0);
        Assert.That(score, Is.EqualTo((100.0 - 80.0) / (100.0 - 60.0)));
    }

    [Test]
    public void CalculateGCScore_AtBoundaries_TreatedAsOutOfRange()
    {
        // The comparison is strict (< upper && > lower), so the boundaries themselves
        // fall through to the proportional branches rather than the "ideal" 1.0 branch.
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.calculateGCScore(40.0, 40.0, 60.0), Is.EqualTo((100.0 - 40.0) / (100.0 - 60.0)));
            Assert.That(SpacerFinder.calculateGCScore(60.0, 40.0, 60.0), Is.EqualTo((100.0 - 60.0) / (100.0 - 60.0)));
        });
    }

    // ===== countHomopolymers =====

    [Test]
    public void CountHomopolymers_NoRuns_ReturnsZero()
    {
        Assert.That(SpacerFinder.countHomopolymers("ACGUACGU"), Is.EqualTo(0));
    }

    [Test]
    public void CountHomopolymers_SingleRun_ReturnsOne()
    {
        Assert.That(SpacerFinder.countHomopolymers("ACAAAAUG"), Is.EqualTo(1));
    }

    [Test]
    public void CountHomopolymers_MultipleRuns_CountsEach()
    {
        Assert.That(SpacerFinder.countHomopolymers("AAAACCCCGGGGUUUU"), Is.EqualTo(4));
    }

    [Test]
    public void CountHomopolymers_ExactlyThreeInARow_DoesNotCount()
    {
        Assert.That(SpacerFinder.countHomopolymers("ACAAAUG"), Is.EqualTo(0));
    }

    [Test]
    public void CountHomopolymers_DnaTRunsAreNotCounted_OnlyRnaU()
    {
        // The regex only matches A/C/G/U runs; a DNA 'T' run should not be flagged.
        Assert.That(SpacerFinder.countHomopolymers("ACTTTTGA"), Is.EqualTo(0));
    }

    // ===== reverse =====

    [Test]
    public void Reverse_ReversesTheSequence()
    {
        Assert.That(SpacerFinder.reverse("ACGU"), Is.EqualTo("UGCA"));
    }

    [Test]
    public void Reverse_EmptyString_ReturnsEmpty()
    {
        Assert.That(SpacerFinder.reverse(""), Is.EqualTo(""));
    }

    // ===== getSeedRegion =====

    [Test]
    public void GetSeedRegion_DefaultRange_ExtractsSubstring()
    {
        var sequence = "ACGUACGUACGUACGUACGUACGUACGU"; // 29 nt
        var seedRegion = SpacerFinder.getSeedRegion(10, 17, sequence);

        Assert.That(seedRegion, Is.EqualTo(sequence.Substring(10, 8)));
    }

    [Test]
    public void GetSeedRegion_RangeLongerThanSequence_ClampsInsteadOfThrowing()
    {
        // Regression test: a 12 nt spacer with the default seed range 10..17 used to throw
        // (F# string slicing is not clamped). It must now clamp into bounds.
        var sequence = "ACGUACGUACGU"; // 12 nt, indices 0..11
        var seedRegion = SpacerFinder.getSeedRegion(10, 17, sequence);

        Assert.That(seedRegion, Is.EqualTo(sequence.Substring(10, 2))); // clamped to 10..11
    }

    [Test]
    public void GetSeedRegion_InvertedRange_ReturnsEmpty()
    {
        var seedRegion = SpacerFinder.getSeedRegion(17, 10, "ACGUACGUACGUACGUACGU");
        Assert.That(seedRegion, Is.EqualTo(""));
    }

    [Test]
    public void GetSeedRegion_EmptySequence_ReturnsEmpty()
    {
        Assert.That(SpacerFinder.getSeedRegion(10, 17, ""), Is.EqualTo(""));
    }

    // ===== getgRNAResult =====

    [Test]
    public void GetgRNAResult_PopulatesMetricsAndDefaults()
    {
        var sequence = "ACGUACGUACGUACGUACGUACGUACGU"; // 29 nt
        var result = SpacerFinder.getgRNAResult(10, 17, sequence);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sequence, Is.EqualTo(sequence));
            Assert.That(result.SeedRegion, Is.EqualTo(sequence.Substring(10, 8)));
            Assert.That(result.GCContent, Is.EqualTo(SpacerFinder.calculateGCContent(sequence)));
            Assert.That(result.GCScore, Is.EqualTo(SpacerFinder.calculateGCScore(result.GCContent, 40.0, 60.0)));
            Assert.That(result.HomopolymerCount, Is.EqualTo(SpacerFinder.countHomopolymers(sequence)));
            // Defaults before Bowtie/RNAFold enrichment:
            Assert.That(result.Allignments, Is.EqualTo(0));
            Assert.That(result.Rank, Is.EqualTo(0));
            Assert.That(result.Score, Is.EqualTo(0.0));
            Assert.That(result.MutationHighlightStart, Is.EqualTo(-1));
            Assert.That(result.MutationHighlightLength, Is.EqualTo(0));
        });
    }

    [Test]
    public void GetgRNAResult_ShortSpacerWithDefaultSeedRange_DoesNotThrow()
    {
        // Same regression as GetSeedRegion, exercised through the full result builder.
        Assert.That(() => SpacerFinder.getgRNAResult(10, 17, "ACGUACGUACGU"), Throws.Nothing);
    }

    // ===== sortByResult =====

    [Test]
    public void SortByResult_OrdersByAllignmentsThenEnergyThenGcScoreThenHomopolymers()
    {
        // Each case changes exactly one field relative to `baseline`, so the expected order
        // directly demonstrates the tuple's field priority: Allignments > Energy > GCScore > Homopolymers.
        var baseline = new SpacerFinder.gRNAResult(
            "AAAA", 1.0, 50.0, 0, "AA", 1,
            new RNAFoldWrapper.RNAFoldResult("....", -1.0), 0, 0.0, -1, 0);
        var worseHomopolymersOnly = new SpacerFinder.gRNAResult(
            "AAAA", 1.0, 50.0, 5, "AA", 1,
            new RNAFoldWrapper.RNAFoldResult("....", -1.0), 0, 0.0, -1, 0);
        var worseGcScoreOnly = new SpacerFinder.gRNAResult(
            "AAAA", 0.2, 10.0, 0, "AA", 1,
            new RNAFoldWrapper.RNAFoldResult("....", -1.0), 0, 0.0, -1, 0);
        var worseEnergyOnly = new SpacerFinder.gRNAResult(
            "AAAA", 1.0, 50.0, 0, "AA", 1,
            new RNAFoldWrapper.RNAFoldResult("....", -5.0), 0, 0.0, -1, 0);
        var worseAllignmentsOnly = new SpacerFinder.gRNAResult(
            "AAAA", 1.0, 50.0, 0, "AA", 5,
            new RNAFoldWrapper.RNAFoldResult("....", -1.0), 0, 0.0, -1, 0);

        var unordered = new[] { worseAllignmentsOnly, worseEnergyOnly, worseGcScoreOnly, worseHomopolymersOnly, baseline };
        var ordered = unordered.OrderBy(SpacerFinder.sortByResult).ToArray();

        Assert.That(ordered, Is.EqualTo(new[]
        {
            baseline, worseHomopolymersOnly, worseGcScoreOnly, worseEnergyOnly, worseAllignmentsOnly
        }));
    }

    // ===== scaffold =====

    [Test]
    public void Scaffold_IsThirtySixNucleotides()
    {
        Assert.That(SpacerFinder.scaffold.Length, Is.EqualTo(36));
    }

    [Test]
    public void Scaffold_HasExpectedSequence()
    {
        Assert.That(SpacerFinder.scaffold, Is.EqualTo("GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC"));
    }
}
