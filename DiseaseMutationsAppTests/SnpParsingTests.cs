using gRNA;
using NUnit.Framework;

namespace DiseaseMutationsAppTests;

/// <summary>The dbSNP refsnp parser, over a trimmed capture of the real rs334 (HBB Glu6Val, sickle cell) response.</summary>
public class SnpParsingTests
{
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "snp", "rs334.json"));

    [Test]
    public void Rs334_ListsEveryAlternativeAlleleOnEveryRefSeqGeneRecord()
    {
        var all = SNP.parseVariants(Json);
        // 4 NG_ records x 3 non-reference alleles, as the capture holds.
        Assert.That(all.Count, Is.EqualTo(12));
        Assert.That(all.Select(v => v.Accession).Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void Rs334_DefaultSelectionIsTheSickleAlleleOnTheMainGeneRecord()
    {
        var preferred = SNP.parseVariants(Json).Where(v => v.Preferred).ToList();
        Assert.That(preferred.Select(v => v.Hgvs), Is.EqualTo(new[] { "NG_000007.3:g.70614A>T" }));
        Assert.That(preferred[0].Significance, Does.Contain("pathogenic"));
    }

    [Test]
    public void Rs334_KnowsTheGeneAndThatItIsReadAlongThePlusStrandOfTheNgRecord()
    {
        // HBB lies on the minus strand of chromosome 11, and the NG_ record is stored in the opposite orientation
        // (gene orientation), so the gene is read along the + strand of NG_000007.3.
        var main = SNP.parseVariants(Json).First(v => v.Preferred);
        Assert.Multiple(() =>
        {
            Assert.That(main.Gene.Value, Is.EqualTo("HBB"));
            Assert.That(main.GeneOnPlusStrand.Value, Is.True);
        });
    }

    [Test]
    public void ReferenceAlleleIsNeverListed()
        => Assert.That(SNP.parseVariants(Json).Any(v => v.Hgvs.EndsWith("=")), Is.False);

    [Test]
    public void HgvsDataTargetsMrna_FollowsTheGeneOrientation()
    {
        // Gene read along + : the Normal tab (spacer complementary to the + strand) binds the mRNA, the Complement tab does not.
        Assert.Multiple(() =>
        {
            Assert.That(new DiseaseMutationsApp.Pages.HgvsData { GeneOnPlusStrand = true, IsComplement = false }.TargetsMrna, Is.True);
            Assert.That(new DiseaseMutationsApp.Pages.HgvsData { GeneOnPlusStrand = true, IsComplement = true }.TargetsMrna, Is.False);
            Assert.That(new DiseaseMutationsApp.Pages.HgvsData { GeneOnPlusStrand = false, IsComplement = true }.TargetsMrna, Is.True);
            Assert.That(new DiseaseMutationsApp.Pages.HgvsData { GeneOnPlusStrand = false, IsComplement = false }.TargetsMrna, Is.False);
            Assert.That(new DiseaseMutationsApp.Pages.HgvsData().TargetsMrna, Is.Null);
        });
    }
}
