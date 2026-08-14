using System.Linq;
using gRNA;

namespace DiseaseMutationsAppTests;

public class SpacerFinderHighlightTests
{
    [Test]
    public void GetMutationHighlightSpan_SubstitutionOverlap_ReturnsIntersection()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(5, 10, 8, 2);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DeletionZeroLength_UsesAnchorInsideWindow()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(10, 8, 13, 0);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DeletionAnchorAtWindowEnd_UsesLastBase()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(2, 5, 7, 0);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(4));
            Assert.That(length, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DuplicationLikeLongRegion_CropsToWindow()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(4, 6, 2, 5);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(0));
            Assert.That(length, Is.EqualTo(3));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_DelinsLikeRegionInsideWindow_ReturnsExactSpan()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(20, 10, 23, 4);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(3));
            Assert.That(length, Is.EqualTo(4));
        });
    }

    [Test]
    public void GetMutationHighlightSpan_NoOverlap_ReturnsNoHighlight()
    {
        var (start, length) = SpacerFinder.getMutationHighlightSpan(0, 5, 20, 3);
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(-1));
            Assert.That(length, Is.EqualTo(0));
        });
    }

    [Test]
    public void AdjustFourthFromEndToAorU_ChangesToA_WhenNotA()
    {
        var adjusted = SpacerFinder.adjustFourthFromEndToAorU("UUUUGG");
        Assert.That(adjusted, Is.EqualTo("UUAUGG"));
    }

    [Test]
    public void ApplySubstitutionSpecialRule_SelectsSingleGrnaAndAdjustsFourthFromEnd()
    {
        var selected = new SpacerFinder.gRNAResult(
            "UUUUGG",
            1.0,
            0,
            0,
            "UUGG",
            3,
            new RNAFoldWrapper.RNAFoldResult("....", -1.2),
            2,
            0.75,
            3,
            1);
        var other = new SpacerFinder.gRNAResult(
            "CCCCCC",
            1.0,
            0,
            0,
            "CCCC",
            2,
            new RNAFoldWrapper.RNAFoldResult("....", -0.8),
            3,
            0.5,
            1,
            1);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { other, selected });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].MutationHighlightStart, Is.EqualTo(3));
            Assert.That(results[0].MutationHighlightLength, Is.EqualTo(1));
            Assert.That(results[0].Sequence, Is.EqualTo("UUAUGG"));
            Assert.That(results[0].Allignments, Is.EqualTo(3));
            Assert.That(results[0].Rank, Is.EqualTo(1));
            Assert.That(results[0].Score, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void ApplySubstitutionSpecialRule_DiscardsAllOtherCandidates_ByDesign()
    {
        // This locks in intentional behavior (documented in BIOLOGICAL_REPORT.md, section 4.4):
        // when the substitution special rule finds a match, it returns ONLY that single
        // mismatch-engineered spacer, not the full ranked candidate list.
        var matching = new SpacerFinder.gRNAResult(
            "UUUUGG",
            1.0,
            0,
            0,
            "UUGG",
            3,
            new RNAFoldWrapper.RNAFoldResult("....", -1.2),
            2,
            0.75,
            3,
            1);
        var others = Enumerable.Range(0, 5)
            .Select(i => new SpacerFinder.gRNAResult(
                $"CCCC{i}G",
                1.0,
                0,
                0,
                "CCCC",
                i,
                new RNAFoldWrapper.RNAFoldResult("....", -0.8 - i),
                i + 2,
                0.5,
                i,
                1))
            .ToArray();

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(others.Append(matching));
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.That(results.Count, Is.EqualTo(1), "Only the mismatch-engineered spacer should survive, all other candidates are discarded.");
    }

    [Test]
    public void ApplySubstitutionSpecialRule_NoMatchingPosition_ReturnsEmpty()
    {
        var nonMatching = new SpacerFinder.gRNAResult(
            "UUUUGG",
            1.0,
            0,
            0,
            "UUGG",
            3,
            new RNAFoldWrapper.RNAFoldResult("....", -1.2),
            2,
            0.75,
            2,
            1);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { nonMatching });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void AdjustFourthFromEndToAorU_ChangesToU_WhenA()
    {
        var adjusted = SpacerFinder.adjustFourthFromEndToAorU("UUAUGG");
        Assert.That(adjusted, Is.EqualTo("UUUUGG"));
    }

    [Test]
    public void AdjustFourthFromEndToAorU_ShorterThanFour_ReturnsUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.adjustFourthFromEndToAorU("UUU"), Is.EqualTo("UUU"));
            Assert.That(SpacerFinder.adjustFourthFromEndToAorU(""), Is.EqualTo(""));
        });
    }

    [Test]
    public void ApplySubstitutionSpecialRule_RecomputesMetricsFromAdjustedSequence_ButKeepsAllignmentsAndFold()
    {
        var selected = new SpacerFinder.gRNAResult(
            "AAAAGG", // GC content 33.33%, adjust index (len-4)=2 'A' -> 'U' => "AAUAGG"
            1.0,
            0,
            0,
            "AAGG",
            5,
            new RNAFoldWrapper.RNAFoldResult("....", -3.4),
            4,
            0.25,
            3,
            1);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { selected });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Sequence, Is.EqualTo("AAUAGG"));
            Assert.That(results[0].GCContent, Is.EqualTo(SpacerFinder.calculateGCContent("AAUAGG")));
            Assert.That(results[0].HomopolymerCount, Is.EqualTo(SpacerFinder.countHomopolymers("AAUAGG")));
            // Carried over from the original selected candidate:
            Assert.That(results[0].Allignments, Is.EqualTo(5));
            Assert.That(results[0].RnaFoldResult.Energy, Is.EqualTo(-3.4));
        });
    }
}
