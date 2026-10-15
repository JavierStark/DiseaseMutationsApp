using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class RestrictionSitesTests
{
    [TestCase("BsaI", "GGTCTC")]
    [TestCase("BsmBI", "CGTCTC")]
    [TestCase("SapI", "GCTCTTC")]
    [TestCase("BbsI", "GAAGAC")]
    [TestCase("EcoRI", "GAATTC")]
    [TestCase("PstI", "CTGCAG")]
    [TestCase("XbaI", "TCTAGA")]
    [TestCase("SpeI", "ACTAGT")]
    [TestCase("EcoRV", "GATATC")]
    [TestCase("NotI", "GCGGCCGC")]
    public void Find_DetectsEachEnzymeOnTheTopStrand(string enzyme, string site)
    {
        Assert.That(RestrictionSites.Find("AAAA" + site + "AAAA"), Is.EqualTo(new[] { enzyme }));
    }

    [TestCase("BsaI", "GGTCTC")]
    [TestCase("BsmBI", "CGTCTC")]
    [TestCase("SapI", "GCTCTTC")]
    [TestCase("BbsI", "GAAGAC")]
    public void Find_DetectsNonPalindromicSitesOnTheBottomStrand(string enzyme, string site)
    {
        var reverseComplement = OligoExport.ReverseComplement(site);
        Assert.That(RestrictionSites.Find("AAAA" + reverseComplement + "AAAA"), Is.EqualTo(new[] { enzyme }));
    }

    [Test]
    public void Find_ReadsRnaAsDna_AndIsCaseInsensitive()
    {
        Assert.That(RestrictionSites.Find("aagaauucaa"), Is.EqualTo(new[] { "EcoRI" }));
    }

    [Test]
    public void Find_ReportsEachEnzymeOnceInListOrder()
    {
        Assert.That(RestrictionSites.Find("GAATTCGAATTCGGTCTC"), Is.EqualTo(new[] { "BsaI", "EcoRI" }));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("ACGTACGTACGT")]
    public void Find_CleanOrEmptyInputHasNoSites(string sequence)
    {
        Assert.That(RestrictionSites.Find(sequence), Is.Empty);
    }

    [Test]
    public void ScaffoldAndT7Promoter_AreCleanOnTheirOwn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RestrictionSites.Find(GrnaService.Scaffold), Is.Empty);
            Assert.That(RestrictionSites.Find(OligoExport.T7Promoter), Is.Empty);
            Assert.That(RestrictionSites.ForGuide("ACGUACGUACGUACGUACGUACGUACGU", true), Is.Empty);
        });
    }

    [Test]
    public void ForGuide_ScansTheWholeConstruct_IncludingTheScaffoldSpacerJunction()
    {
        // The scaffold ends in ...AAAAC, so a spacer starting GUCUC puts CGTCTC (BsmBI) across the junction
        // although neither the scaffold nor the spacer contains the site by itself.
        var junctionSpacer = "GUCUCACGUACGUACGUACGUACGUACG";
        Assert.Multiple(() =>
        {
            Assert.That(RestrictionSites.Find(junctionSpacer), Is.Empty);
            Assert.That(RestrictionSites.ForGuide(junctionSpacer, false), Is.EqualTo(new[] { "BsmBI" }));
        });
    }

    [Test]
    public void Warning_UsesTheExactMessage()
    {
        Assert.That(RestrictionSites.Warning("BsaI"),
            Is.EqualTo("Warning: This DNA sequence includes a restriction site for the enzyme: 'BsaI'"));
    }

    [Test]
    public void Compatibility_IsGreenOnlyWhenNoEnzymeOfTheStandardCuts()
    {
        var none = RestrictionSites.Compatibility(Array.Empty<string>());
        Assert.That(none.Select(c => c.Standard.Rfc), Is.EqualTo(new[] { "10", "1000" }));
        Assert.That(none.All(c => c.IsCompatible));

        var bsa = RestrictionSites.Compatibility(new[] { "BsaI" });
        Assert.That(bsa[0].IsCompatible, Is.True);
        Assert.That(bsa[1].IsCompatible, Is.False);

        var eco = RestrictionSites.Compatibility(new[] { "EcoRI", "BsmBI", "EcoRV" });
        Assert.That(eco[0].Blocking, Is.EqualTo(new[] { "EcoRI" }));
        Assert.That(eco[1].IsCompatible, Is.True);
    }
}
