using DiseaseMutationsApp.Services;
using NUnit.Framework;

namespace DiseaseMutationsAppTests;

public class GuideConstructTests
{
    private const string Spacer = "ACGUACGUACGUACGUACGUACGUACGU";
    private static string Rna(string dna) => dna.Replace('T', 'U');

    [Test]
    public void Default_IsTheLwaDirectRepeatPlusSpacer()
    {
        Assert.That(GuideConstruct.Build(Spacer), Is.EqualTo(GrnaService.Scaffold + Spacer));
    }

    [Test]
    public void Frida_WrapsTheDrAndSpacer()
    {
        var built = GuideConstruct.Build(Spacer, new ConstructOptions(Frida: true));
        Assert.That(built, Is.EqualTo(Rna(GuideConstruct.Frida5) + GrnaService.Scaffold + Spacer + Rna(GuideConstruct.Frida3)));
    }

    [Test]
    public void BioBricks_AloneWrapsTheDrAndSpacer()
    {
        var built = GuideConstruct.Build(Spacer, new ConstructOptions(BioBricks: true));
        Assert.That(built, Is.EqualTo(Rna(GuideConstruct.BioBricks5) + GrnaService.Scaffold + Spacer + Rna(GuideConstruct.BioBricks3)));
    }

    [Test]
    public void BioBricks_AlwaysWrapsFridaWhenBothAreOn()
    {
        var built = GuideConstruct.Build(Spacer, new ConstructOptions(Frida: true, BioBricks: true));
        Assert.That(built, Is.EqualTo(
            Rna(GuideConstruct.BioBricks5) + Rna(GuideConstruct.Frida5) + GrnaService.Scaffold + Spacer
            + Rna(GuideConstruct.Frida3) + Rna(GuideConstruct.BioBricks3)));
    }

    [Test]
    public void NoDr_LeavesOnlyTheWrappers()
    {
        var built = GuideConstruct.Build(Spacer, new ConstructOptions(DrId: GuideConstruct.NoneId, Frida: true));
        Assert.That(built, Is.EqualTo(Rna(GuideConstruct.Frida5) + Spacer + Rna(GuideConstruct.Frida3)));
    }

    [Test]
    public void CustomFlanks_SitInsideTheAdapters_AndEitherSideMayBeEmpty()
    {
        var both = GuideConstruct.Build(Spacer, new ConstructOptions(DrId: GuideConstruct.NoneId, Frida: true, Custom5: "gg tt", Custom3: ""));
        Assert.That(both, Is.EqualTo(Rna(GuideConstruct.Frida5) + "GGUU" + Spacer + Rna(GuideConstruct.Frida3)));
    }

    [Test]
    public void CustomFlanks_AreConvertedToRna()
    {
        var built = GuideConstruct.Build(Spacer, new ConstructOptions(DrId: GuideConstruct.NoneId, Custom3: "TTA"));
        Assert.That(built, Is.EqualTo(Spacer + "UUA"));
    }

    [Test]
    public void InvalidCustomText_IsRejectedAndIgnored()
    {
        Assert.That(GuideConstruct.IsValidCustom("ACGTUacgtu "), Is.True);
        Assert.That(GuideConstruct.IsValidCustom("ACGX"), Is.False);
        Assert.That(GuideConstruct.Build(Spacer, new ConstructOptions(DrId: GuideConstruct.NoneId, Custom5: "ACGX")), Is.EqualTo(Spacer));
    }

    [Test]
    public void Describe_NamesEveryPiece()
    {
        Assert.That(GuideConstruct.Describe(new ConstructOptions(Frida: true, BioBricks: true, Custom3: "AA")),
            Is.EqualTo("LwaCas13a DR + Frida + BioBricks + custom 3'"));
        Assert.That(GuideConstruct.Describe(new ConstructOptions(DrId: GuideConstruct.NoneId)), Is.EqualTo("no DR"));
    }

    [Test]
    public void AdapterSequences_AreTheOnesSpecified()
    {
        Assert.That(GuideConstruct.Frida5 + "-" + GuideConstruct.Frida3, Is.EqualTo("AGAGCGTCTCTAAAC-GCTTAGAGACGTACT"));
        Assert.That(GuideConstruct.BioBricks5 + "-" + GuideConstruct.BioBricks3, Is.EqualTo("GGCTAGCGAATTCGCGGCCGCTTCT-AGTAGCGGCCGCTGCAGCGATCGT"));
    }

    [Test]
    public void OligoTemplate_UsesTheBuiltConstructAsDna()
    {
        var o = new ConstructOptions(Frida: true);
        Assert.That(OligoExport.Template(Spacer, false, OligoStrand.Sense, o), Is.EqualTo(GuideConstruct.Build(Spacer, o).Replace('U', 'T')));
        Assert.That(OligoExport.Template(Spacer, true, OligoStrand.Sense, o), Does.StartWith(OligoExport.T7Promoter + GuideConstruct.Frida5));
    }

    [Test]
    public void Fasta_HasHeaderAndCompleteSequence()
    {
        var o = new ConstructOptions(Frida: true);
        var items = new[] { new ShortlistItem("NG_1:g.1A>T", true, Spacer, "1", 1, 1) };
        var dna = OligoExport.ShortlistFasta(items, o);
        var lines = dna.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Does.StartWith(">NG_1:g.1A>T_C|spacer=" + Spacer + "|construct=LwaCas13a DR + Frida|DNA|"));
        Assert.That(lines[1], Is.EqualTo(GuideConstruct.Build(Spacer, o).Replace('U', 'T')));
        Assert.That(OligoExport.ShortlistFasta(items, o, rna: true), Does.Contain(GuideConstruct.Build(Spacer, o) + "\n"));
    }

    [Test]
    public void AdapterOwnSites_AreNotReported_ButSitesInTheSpacerStillAre()
    {
        var o = new ConstructOptions(Frida: true, BioBricks: true);
        Assert.That(RestrictionSites.ForGuide(Spacer, false, o), Is.Empty);
        // a BsmBI site inside the spacer would break the Golden Gate step even though the adapter has one by design
        Assert.That(RestrictionSites.ForGuide("ACGUCGUCUCACGUACGUACGUACGUAC", false, o), Does.Contain("BsmBI"));
        // BioBricks adapter alone must not mark the guide as BioBrick-incompatible
        Assert.That(RestrictionSites.ForGuide(Spacer, false, new ConstructOptions(BioBricks: true)), Is.Empty);
    }

    [Test]
    public void SessionDocument_RoundTripsConstruct_AndOldFilesLoadWithoutIt()
    {
        var doc = new SessionDocument(SessionDocument.CurrentVersion, "rs1", 28, "10-17", new(), new ConstructOptions(Frida: true, Custom5: "AA"));
        var back = SessionDocument.TryParse(SessionDocument.Serialize(doc));
        Assert.That(back!.Construct, Is.EqualTo(doc.Construct));
        var old = SessionDocument.TryParse("{\"version\":1,\"input\":\"rs9\",\"spacer\":28,\"seed\":\"10-17\",\"shortlist\":[]}");
        Assert.That(old, Is.Not.Null);
        Assert.That(old!.Construct, Is.Null);
    }
}
