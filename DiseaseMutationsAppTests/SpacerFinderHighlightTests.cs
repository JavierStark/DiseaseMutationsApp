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
            1, -1, false, true, -1.0);
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
            1, -1, false, true, -1.0);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { other, selected });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count, Is.EqualTo(3), "the engineered spacer comes first, the two ordinary candidates follow");
            Assert.That(results[0].Engineered, Is.True);
            Assert.That(results[1].Engineered, Is.False);
            Assert.That(results[2].Engineered, Is.False);
            Assert.That(results[0].MutationHighlightStart, Is.EqualTo(3));
            Assert.That(results[0].MutationHighlightLength, Is.EqualTo(1));
            Assert.That(results[0].Sequence, Is.EqualTo("UUAUGG"));
            Assert.That(results[0].Allignments, Is.EqualTo(3));
            Assert.That(results[0].Rank, Is.EqualTo(1));
            Assert.That(results[0].Score, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void ApplySubstitutionSpecialRule_KeepsTheOrdinaryRankingAfterTheEngineeredSpacer()
    {
        // Decision 2026-10-16 (BIOLOGICAL_REPORT.md, section 4.4): the engineered spacer is listed FIRST and highlighted,
        // and the whole ordinary ranked list follows it unchanged (before, it replaced the list).
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
            1, -1, false, true, -1.0);
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
                1, -1, false, true, -1.0))
            .ToArray();

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(others.Append(matching));
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count, Is.EqualTo(7), "engineered spacer + the 6 ordinary candidates (the selected one is kept unmodified too)");
            Assert.That(results[0].Engineered, Is.True);
            Assert.That(results.Skip(1).Any(r => r.Engineered), Is.False);
            Assert.That(results.Skip(1).Select(r => r.Sequence), Is.EqualTo(input.Select(r => r.Sequence)), "ordinary list unchanged and in order");
        });
    }

    [Test]
    public void ApplySubstitutionSpecialRule_NoMatchingPosition_KeepsTheRankedList()
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
            1, -1, false, true, -1.0);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { nonMatching });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        // Decision 2026-10-13: when no spacer carries the mutation at N-3 the ranked list is kept, not emptied.
        Assert.Multiple(() =>
        {
            Assert.That(results.Length, Is.EqualTo(1));
            Assert.That(results[0].Sequence, Is.EqualTo("UUUUGG"));
            Assert.That(results[0].Rank, Is.EqualTo(2), "the candidate is returned untouched");
            Assert.That(SpacerFinder.specialRuleApplies(6, input), Is.False);
        });
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
            1, -1, false, true, -1.0);

        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { selected });
        var results = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Sequence, Is.EqualTo("AAUAGG"));
            Assert.That(results[1].Sequence, Is.EqualTo("AAAAGG"), "the ordinary candidate stays after the engineered one");
            Assert.That(results[0].GCContent, Is.EqualTo(SpacerFinder.calculateGCContent("AAUAGG")));
            Assert.That(results[0].HomopolymerCount, Is.EqualTo(SpacerFinder.countHomopolymers("AAUAGG")));
            // Carried over from the original selected candidate:
            Assert.That(results[0].Allignments, Is.EqualTo(5));
            Assert.That(results[0].RnaFoldResult.Energy, Is.EqualTo(-3.4));
        });
    }

    [Test]
    public void MinMismatchesAgainst_CountsTheClosestWindowOfTheOtherSequence()
    {
        // The window ACGTACGT differs from the closest stretch of the other sequence by exactly one letter.
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.minMismatchesAgainst("ACGTACGT", "TTTTACGTACGATTTT"), Is.EqualTo(1));
            Assert.That(SpacerFinder.minMismatchesAgainst("ACGTACGT", "GGACGTACGTGG"), Is.EqualTo(0));
            Assert.That(SpacerFinder.minMismatchesAgainst("AAAA", "CCCCCCCC"), Is.EqualTo(4));
            Assert.That(SpacerFinder.minMismatchesAgainst("AAAAAA", "AAA"), Is.EqualTo(6), "nothing to compare with: the whole window differs");
        });
    }

    [Test]
    public void WindowOfSpacer_IsTheReverseComplementAsDna()
    {
        // spacer = reverse(complement(window)) with T -> U, so the window is the reverse complement of the spacer.
        Assert.That(SpacerFinder.windowOfSpacer("CAGGGCAGUAAC"), Is.EqualTo("GTTACTGCCCTG"));
    }

    [Test]
    public void AnnotateDiscrimination_SeparatesAGuideThatTellsTheAllelesApart()
    {
        // Sickle cell (HBB Glu6Val): the normal and mutated 12-nt windows differ at one letter.
        var normal = "CCTGAGGAGAAG";
        var mutated = "CCTGTGGAGAAG";
        var spacerOfMutated = SpacerFinder.reverse(Sequence.complementary(mutated)).Replace('T', 'U');
        var guide = SpacerFinder.getgRNAResult(0, 3, spacerOfMutated);
        var annotated = SpacerFinder.annotateDiscrimination(normal, Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { guide }));
        Assert.That(annotated[0].WtMismatches, Is.EqualTo(1), "one letter apart: a single-letter variant cannot be told apart without engineering");
    }
}
