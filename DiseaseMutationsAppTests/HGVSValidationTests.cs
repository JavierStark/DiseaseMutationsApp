using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>Input validation of the HGVS parser: every malformed input must raise GrnaInputException (CLI exit 2).</summary>
public class HGVSValidationTests
{
    [TestCase("   ")]
    [TestCase("NC_000017.11:g.")]
    [TestCase("NC_000017.11:g.7674220")]                // position without a change is not a change
    [TestCase("NC_000017.11:7674220C>T")]               // missing the "g."
    [TestCase("NC_000017.11:g.7674220C>T:extra")]
    [TestCase(":g.7674220C>T")]
    [TestCase("NC_000017.11:g.99999999999C>T")]         // overflows Int32
    [TestCase("NC_000017.11:g.0C>T")]                   // positions are 1-based
    [TestCase("NC_000017.11:g.7674222_7674220del")]     // reversed range
    [TestCase("NC_000017.11:g.7674220c>t")]             // lower-case alleles
    [TestCase("NC_000017.11:g.7674220N>T")]             // IUPAC codes
    [TestCase("NC_000017.11:g.7674220C>C")]             // not a change
    [TestCase("NC_000017.11:g.7674220CA>T")]            // a substitution changes one base
    [TestCase("NC_000017.11:g.7674220_7674221ins")]     // nothing inserted
    [TestCase("NC_000017.11:g.7674220_7674221insxyz")]
    [TestCase("NC_000017.11:g.7674220_7674222delinsAXG")]
    [TestCase("NC_000017.11:g.7674220_7674222delxyz")]  // stated deleted sequence must be bases
    [TestCase("NM_000546.6:c.2TA[x]")]                  // repeat count is not a number
    [TestCase("NM_000546.6:c.2TA[99999999999]")]
    public void MalformedHgvs_ThrowsTypedInputException(string code)
    {
        Assert.Throws<GrnaInputException>(() => _ = new HGVS.HGVS(code));
    }

    [Test]
    public void ExplicitDeletedSequence_IsKeptAsReference()
    {
        var sut = new HGVS.HGVS("NC_000017.11:g.7674220_7674222delACG");
        Assert.Multiple(() =>
        {
            Assert.That(sut.Mutation, Is.EqualTo(HGVS.MutationType.Deletion));
            Assert.That(sut.Reference, Is.EqualTo("ACG"));
        });
    }

    [Test]
    public void DeletionWithoutStatedSequence_HasEmptyReference()
    {
        Assert.That(new HGVS.HGVS("NC_000017.11:g.7674220_7674222del").Reference, Is.Empty);
    }

    [Test]
    public void LargestValidPosition_Parses()
    {
        var sut = new HGVS.HGVS($"NC_000017.11:g.{int.MaxValue}C>T");
        Assert.That(sut.Position.Item1, Is.EqualTo(int.MaxValue));
    }
}
