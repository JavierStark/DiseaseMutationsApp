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
}
