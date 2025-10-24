using DiseaseMutationsApp;

namespace DiseaseMutationsAppTests;

public class HGVSTests
{
    [Test]
    public void ParseBasicData()
    {
        var sut = new HGVS("NM_000546.6:c.215C>G");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Accession, Is.EqualTo("NM_000546.6"));
            Assert.That(sut.Type, Is.EqualTo("c"));
        });
    }
/*
    Substitution
    Deletion    
    Insertion
    Duplication
    Inversion
    Deletion–Insertion
    Repeat
    No change
    Conversion	
    Translocation / insertion from another seq
    Large unknown
*/
    [Test]
    public void ParsePosition()
    {
        var sut = new HGVS("NM_000546.6:c.215C>G");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Position.start, Is.EqualTo(215));
            Assert.That(sut.Position.end, Is.EqualTo(215));
        });
    }

    [Test]
    public void ParseRangePosition()
    {
        var sut = new HGVS("NM_000546.6:c.215_217del");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Position.start, Is.EqualTo(215));
            Assert.That(sut.Position.end, Is.EqualTo(217));
        });
    }
    
    [Test]
    public void ParseSubstitution()
    {
        var sut = new HGVS("NM_000546.6:c.215C>G");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Substitution));
            Assert.That(sut.Reference, Is.EqualTo("C"));
            Assert.That(sut.Alternate, Is.EqualTo("G"));
        });
    }
    
    [Test]
    public void ParseDeletion()
    {
        var sut = new HGVS("NM_000546.6:c.215_217del");
        Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Deletion));
    }
    
    [Test]
    public void ParseInsertion()
    {
        var sut = new HGVS("NM_000546.6:c.215_216insA");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Insertion));
            Assert.That(sut.Alternate, Is.EqualTo("A"));
        });
    }
    
    [Test]
    public void ParseDuplication()
    {
        var sut = new HGVS("NM_000546.6:c.215_217dup");
        Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Duplication));
    }
    
    [Test]
    public void ParseDeletionInsertion()
    {
        var sut = new HGVS("NM_000546.6:c.215_217delinsAG");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.DeletionInsertion));
            Assert.That(sut.Alternate, Is.EqualTo("AG"));
        });
    }
    
    [Test]
    public void ParseRepeat()
    {
        var sut = new HGVS("NM_000546.6:c.215_217TA[3]");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Repeat));
            Assert.That(sut.Alternate, Is.EqualTo("TA"));
            Assert.That(sut.Count, Is.EqualTo(3));
        });
    }
    
    [Test]
    public void ParseInversion()
    {
        var sut = new HGVS("NM_000546.6:c.215_217inv");
        Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Inversion));
    }
    
    [Test]
    public void ParseNoChange()
    {
        var sut = new HGVS("NM_000546.6:c.215=");
        Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.NoChange));
    }
}