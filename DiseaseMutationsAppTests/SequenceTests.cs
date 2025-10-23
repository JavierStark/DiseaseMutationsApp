using DiseaseMutationsApp;

namespace DiseaseMutationsAppTests;

public class SequenceTests
{
    [Test]
    public void GetOriginalNucleotid(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5G>A");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(original, Is.EqualTo("G"));
    }
    
    [Test]
    public void GetOriginalRange(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5_7del");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(original, Is.EqualTo("GTA"));
    }
    
    [Test]
    public void GetOriginalWithBorders(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5_7del");
        var (_, original) = sequence.GetMutatedSubsequence(hgvs, 2,2);
        Assert.That(original, Is.EqualTo("GCGTACG"));
    }
    
    [Test]
    public void GetMutatedNoChange(){
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5=");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(mutated, Is.EqualTo("G"));
    }

    [Test]
    public void GetMutatedSubstitution()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5G>A");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(mutated, Is.EqualTo("A"));
    }

    [Test]
    public void GetMutatedSubstitutionWithBorders()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5G>A");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs, 2, 2);
        Assert.That(mutated, Is.EqualTo("GCATA"));
    }
    
    [Test]
    public void GetMutatedDeletion()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5del");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(mutated, Is.EqualTo("ATGCTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC"));
    }
    
    [Test]
    public void GetMutatedDeletionRange()
    {
        const string seqData = "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC";
        var sequence = new Sequence("NG_016465.4", seqData);
        var hgvs = new HGVS("NG_016465.4:c.5_7del");
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs);
        Assert.That(mutated, Is.EqualTo("ATGCCGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC"));
    }
}