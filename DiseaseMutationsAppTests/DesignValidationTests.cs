using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>Pure checks that run before any Bowtie or NCBI work: spacer bounds, variant vs fetched sequence, strand orientation.</summary>
public class DesignValidationTests
{
    // Position 11 is 'C' in this sequence.
    private const string Data = "ATGCGTACGTCAGCTAGCTAGCTAGC";

    private static HGVS.HGVS H(string code) => new(code);

    [TestCase(18, 0, 17)]
    [TestCase(28, 10, 17)]
    [TestCase(60, 0, 0)]
    public void ValidSpacerParameters_Pass(int size, int seedStart, int seedEnd)
    {
        Assert.That(() => Main.validateSpacerParameters(size, seedStart, seedEnd), Throws.Nothing);
    }

    [TestCase(17, 0, 5)]
    [TestCase(61, 0, 5)]
    [TestCase(0, 0, 0)]
    [TestCase(-28, 0, 0)]
    [TestCase(28, -1, 5)]
    [TestCase(28, 20, 5)]
    [TestCase(28, 10, 28)]
    public void InvalidSpacerParameters_ThrowInputException(int size, int seedStart, int seedEnd)
    {
        Assert.Throws<GrnaInputException>(() => Main.validateSpacerParameters(size, seedStart, seedEnd));
    }

    [TestCase("NC_000001.11:g.11C>T")]
    [TestCase("NM_000001.1:n.11C>T")]
    [TestCase("NC_012920.1:m.11C>T")]
    [TestCase("NC_000001.11:g.11_13delCAG")]
    [TestCase("NC_000001.11:g.11_13del")]
    [TestCase("NC_000001.11:g.11_12insA")]
    [TestCase("NC_000001.11:g.11_13delinsT")]
    [TestCase("NC_000001.11:g.26C>T")]               // last base is allowed
    public void SupportedVariants_Pass(string code)
    {
        // Data[25] is 'C' (last base), Data[10..12] = "CAG".
        Assert.That(() => Main.validateAgainstSequence(H(code), Data), Throws.Nothing);
    }

    [Test]
    public void ReferenceMismatch_Throws_AndNamesBothBases()
    {
        var ex = Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NC_000001.11:g.11A>T"), Data))!;
        Assert.That(ex.Message, Does.Contain("states A").And.Contain("has C"));
    }

    [Test]
    public void ReferenceCheck_IsCaseInsensitiveOnTheSequence()
    {
        Assert.That(() => Main.validateAgainstSequence(H("NC_000001.11:g.11C>T"), Data.ToLowerInvariant()), Throws.Nothing);
    }

    [Test]
    public void StatedDeletedSequence_MustMatch()
    {
        Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NC_000001.11:g.11_13delTTT"), Data));
    }

    [Test]
    public void PositionBeyondTheSequence_Throws()
    {
        var ex = Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NC_000001.11:g.27C>T"), Data))!;
        Assert.That(ex.Message, Does.Contain("outside"));
    }

    [Test]
    public void CodingPositions_AreRejected_WithAClearReason()
    {
        var ex = Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NM_000546.6:c.11C>T"), Data))!;
        Assert.That(ex.Message, Does.Contain("c.").And.Contain("CDS"));
    }

    [Test]
    public void ProteinPositions_AreRejected()
    {
        Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NP_000537.3:p.11C>T"), Data));
    }

    [Test]
    public void RepeatNotation_IsRejectedAtDesignTime()
    {
        Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NC_000001.11:g.11TA[3]"), Data));
    }

    [Test]
    public void InsertionNeedsAdjacentPositions()
    {
        Assert.Throws<GrnaInputException>(() => Main.validateAgainstSequence(H("NC_000001.11:g.11_14insA"), Data));
    }

    // Strand orientation (H1): the complement tab is the reverse complement, read 5'->3'.
    [Test]
    public void ReverseComplement_ReversesAndComplements()
    {
        Assert.That(Sequence.reverseComplement("AAAC"), Is.EqualTo("GTTT"));
    }

    [TestCase("ATGCGTACGTAGC")]
    [TestCase("AAAAAAAA")]
    [TestCase("")]
    public void ReverseComplement_IsAnInvolution(string s)
    {
        Assert.That(Sequence.reverseComplement(Sequence.reverseComplement(s)), Is.EqualTo(s));
    }

    [Test]
    public void MirrorSpan_MovesTheMutationToTheOtherEnd()
    {
        // "AAAC": the C is at (3,1); in the reverse complement "GTTT" the G is at (0,1).
        Assert.That(Main.mirrorSpan(4, 3, 1), Is.EqualTo(Tuple.Create(0, 1)));
    }

    [TestCase(10, 0, 3)]
    [TestCase(10, 4, 1)]
    [TestCase(10, 7, 3)]
    [TestCase(10, 5, 0)]    // zero-length span = deletion junction
    public void MirrorSpan_IsAnInvolution(int total, int start, int length)
    {
        var once = Main.mirrorSpan(total, start, length);
        Assert.That(Main.mirrorSpan(total, once.Item1, once.Item2), Is.EqualTo(Tuple.Create(start, length)));
    }

    [Test]
    public void OrientForStrand_Normal_ChangesNothing()
    {
        var (seq, span) = Main.orientForStrand(false, "AAAC", 3, 1);
        Assert.Multiple(() =>
        {
            Assert.That(seq, Is.EqualTo("AAAC"));
            Assert.That(span, Is.EqualTo(Tuple.Create(3, 1)));
        });
    }

    [Test]
    public void OrientForStrand_Complement_ReverseComplementsAndRemapsTheSpan()
    {
        var (seq, span) = Main.orientForStrand(true, "AAAC", 3, 1);
        Assert.Multiple(() =>
        {
            Assert.That(seq, Is.EqualTo("GTTT"));
            Assert.That(span, Is.EqualTo(Tuple.Create(0, 1)));
            // The highlighted base of the complement sequence is the complement of the original mutated base.
            Assert.That(seq.Substring(span.Item1, span.Item2), Is.EqualTo("G"));
        });
    }
}
