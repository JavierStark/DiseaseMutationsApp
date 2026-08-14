using gRNA;

namespace DiseaseMutationsAppTests;

public class SequenceTests
{
    [Test]
    public void GetOriginalNucleotid(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5G>A");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 0, 0);
        Assert.That(original, Is.EqualTo("G"));
    }
    
    [Test]
    public void GetOriginalRange(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7del");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 0, 0);
        Assert.That(original, Is.EqualTo("GTA"));
    }
    
    [Test]
    public void GetOriginalWithBorders(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7del");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 2,2);
        Assert.That(original, Is.EqualTo("GCGTACG"));
    }
    
    [Test]
    public void GetMutatedNoChange(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5=");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,0,0);
        Assert.That(mutated, Is.EqualTo("G"));
    }

    [Test]
    public void GetMutatedSubstitution()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5G>A");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,0,0);
        Assert.That(mutated, Is.EqualTo("A"));
    }

    [Test]
    public void GetMutatedSubstitutionWithBorders()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5G>A");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs, 2, 2);
        Assert.That(mutated, Is.EqualTo("GCATA"));
    }
    
    [Test]
    public void GetMutatedDeletion()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5del");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,1,1);
        Assert.That(mutated, Is.EqualTo("CT"));
    }
    
    [Test]
    public void GetMutatedDeletionRange()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7del");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,1,1);
        Assert.That(mutated, Is.EqualTo("CC"));
    }

    [Test]
    public void GetMutatedInsertion()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_6insA");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,1,1);
        Assert.That(mutated, Is.EqualTo("CGATA"));
    }
    
    [Test]
    public void GetMutatedDuplication()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7dup");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,0,0);
        Assert.That(mutated, Is.EqualTo("GTAGTA"));
    }

    [Test]
    public void GetMutatedInversion()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7inv");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,1,1);
        Assert.That(mutated, Is.EqualTo("CATGC"));
    }

    // [Test]
    // public void GetMutatedRepeat()
    // {
    //     const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
    //     var sequence = new Sequence("NM_000546.6", seqData);
    //     var hgvs = new HGVS("NM_000546.6:c.2TA[3]");
    //     var (mutated, _) = sequence.GetMutatedSubsequence(hgvs,1,1);
    //     Assert.That(mutated, Is.EqualTo("TTATATATAG"));
    // }

    [Test]
    public void GetMutatedDeletionInsertion()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7delinsAG");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs, 0, 0);
        Assert.That(mutated, Is.EqualTo("AG"));
    }

    [Test]
    public void GetOriginalWithBorders_ClampedAtSequenceStart()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        // Position 1 with left padding of 5: there is nothing to the left, so it should clamp
        // to the start of the sequence rather than throwing.
        var hgvs = new HGVS.HGVS("NG_016465.4:c.1G>A");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 5, 0);
        Assert.That(original, Is.EqualTo("A"));
    }

    [Test]
    public void GetOriginalWithBorders_ClampedAtSequenceEnd()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC"; // length 41
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        // Position 41 (the last base) with right padding of 10: nothing to the right,
        // should clamp to the end of the sequence rather than throwing.
        var hgvs = new HGVS.HGVS("NG_016465.4:c.41C>A");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 0, 10);
        Assert.That(original, Is.EqualTo("C"));
    }

    [Test]
    public void GetMutatedNoChangeWithPadding_ReturnsOriginalWithBorders()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence.Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5=");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs, 2, 2);
        Assert.That(mutated, Is.EqualTo("GCGTA"));
    }

    [Test]
    public void Complementary_MapsEachBase()
    {
        Assert.That(Sequence.complementary("ATCG"), Is.EqualTo("TAGC"));
    }

    [Test]
    public void Complementary_UnknownCharacter_PassesThrough()
    {
        Assert.That(Sequence.complementary("ATNCG"), Is.EqualTo("TANGC"));
    }

    [Test]
    public void Complementary_EmptyString_ReturnsEmpty()
    {
        Assert.That(Sequence.complementary(""), Is.EqualTo(""));
    }

    [Test]
    public void Complementary_IsItsOwnInverse()
    {
        const string original = "ATGCGTACGTAGCTAGC";
        Assert.That(Sequence.complementary(Sequence.complementary(original)), Is.EqualTo(original));
    }
}
