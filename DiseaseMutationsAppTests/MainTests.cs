using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Tests for the pure helper in Main.fs that computes where the mutation falls inside the
/// extracted mutated/original subsequence, clamped by however much flanking context (padding)
/// was actually available near the sequence boundaries.
/// </summary>
public class MainTests
{
    [Test]
    public void CalculateMutationSpanInMutated_MutationInMiddle_UsesFullExtraNucleotidsOnBothSides()
    {
        var hgvs = new HGVS.HGVS("NM_000546.6:c.50G>A"); // position (50, 50)
        var (leftContext, mutationLength) = Main.calculateMutationSpanInMutated(10, 100, hgvs, 21);

        Assert.Multiple(() =>
        {
            Assert.That(leftContext, Is.EqualTo(10));
            Assert.That(mutationLength, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateMutationSpanInMutated_ClampedAtSequenceStart()
    {
        var hgvs = new HGVS.HGVS("NM_000546.6:c.3G>A"); // position (3, 3), only 2 bases to the left
        var (leftContext, mutationLength) = Main.calculateMutationSpanInMutated(10, 100, hgvs, 13);

        Assert.Multiple(() =>
        {
            Assert.That(leftContext, Is.EqualTo(2));
            Assert.That(mutationLength, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateMutationSpanInMutated_ClampedAtSequenceEnd()
    {
        var hgvs = new HGVS.HGVS("NM_000546.6:c.98G>A"); // position (98, 98), only 2 bases to the right in a 100 nt sequence
        var (leftContext, mutationLength) = Main.calculateMutationSpanInMutated(10, 100, hgvs, 13);

        Assert.Multiple(() =>
        {
            Assert.That(leftContext, Is.EqualTo(10));
            Assert.That(mutationLength, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateMutationSpanInMutated_DeletionLeavingNoResidue_ReturnsZeroLengthSpan()
    {
        var hgvs = new HGVS.HGVS("NM_000546.6:c.40_42del"); // position (40, 42), fully deleted
        var (leftContext, mutationLength) = Main.calculateMutationSpanInMutated(5, 100, hgvs, 10);

        Assert.Multiple(() =>
        {
            Assert.That(leftContext, Is.EqualTo(5));
            Assert.That(mutationLength, Is.EqualTo(0));
        });
    }

    [TestCase("NM_000546.6:c.50G>A", 10, 100, 40, 60)]
    [TestCase("NM_000546.6:c.3G>A", 10, 100, 1, 13)]
    [TestCase("NM_000546.6:c.98G>A", 10, 100, 88, 100)]
    [TestCase("NM_000546.6:c.40_42del", 5, 100, 35, 47)]
    public void CalculateWindow_IsOneBasedInclusiveAndClampedToTheSequence(string code, int extra, int length, int start, int end)
    {
        var (s, e) = Main.calculateWindow(extra, length, new HGVS.HGVS(code));
        Assert.Multiple(() =>
        {
            Assert.That(s, Is.EqualTo(start));
            Assert.That(e, Is.EqualTo(end));
        });
    }

    [Test]
    public void CalculateWindow_MatchesTheLengthOfTheSliceTakenFromTheSequence()
    {
        var hgvs = new HGVS.HGVS("NM_000546.6:c.3G>A");
        var data = new string('A', 100);
        var sequence = new Sequence.Sequence("test", data);
        var original = sequence.GetMutatedSubsequence(hgvs, 10, 10).Item2;
        var window = Main.calculateWindow(10, data.Length, hgvs);
        Assert.That(window.Item2 - window.Item1 + 1, Is.EqualTo(original.Length));
    }
}

/// <summary>The warning that goes with the substitution rule falling back to the ordinary ranking.</summary>
public class SpecialRuleNoticeTests
{
    private static HGVS.HGVS Sub(int position) => new($"NG_000007.3:g.{position}C>T");

    [Test]
    public void RuleAppliedToBoth_NoNotice()
    {
        Assert.That(Main.specialRuleNotice(Sub(500), 1000, 28, true, true), Is.EqualTo(Microsoft.FSharp.Core.FSharpOption<string>.None));
    }

    [TestCase(false, true, "mutated sequence")]
    [TestCase(true, false, "original sequence")]
    [TestCase(false, false, "mutated and original sequences")]
    public void RuleNotApplied_NamesTheSequence_AndGivesTheGeometry(bool mutated, bool original, string which)
    {
        var notice = Main.specialRuleNotice(Sub(1), 1000, 28, mutated, original)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(notice, Does.Contain(which));
            Assert.That(notice, Does.Contain("position 1 of NG_000007.3 (1000 bases)"));
            Assert.That(notice, Does.Contain("at least 8 and 19 bases"));
            Assert.That(notice, Does.Contain("ordinary ranking is shown instead"));
        });
    }

    [Test]
    public void TheNoticeFollowsTheSpacerSize()
    {
        Assert.That(Main.specialRuleNotice(Sub(5), 100, 20, false, false)!.Value, Does.Contain("at least 8 and 11 bases"));
    }
}
