using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class OligoExportTests
{
    [Test]
    public void Template_SenseIsTheTranscriptAsDna_WithOptionalT7()
    {
        var spacer = "CAGAGCGCGCAGCAGCAGCAGCAGAAGC";
        var plain = OligoExport.Template(spacer, false, OligoStrand.Sense);
        var withT7 = OligoExport.Template(spacer, true, OligoStrand.Sense);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo((GrnaService.Scaffold + spacer).Replace('U', 'T')));
            Assert.That(plain, Does.Not.Contain("U"));
            Assert.That(withT7, Is.EqualTo(OligoExport.T7Promoter + plain));
            Assert.That(withT7, Does.Contain("TAATACGACTCACTATAGATTTAG"), "the gRNA's first G is the T7 +1 base");
        });
    }

    [Test]
    public void Antisense_IsTheReverseComplement_AndPairsAnneal()
    {
        var pair = OligoExport.Pair("v", "ACGUACGUACGUACGUACGUACGUACGU", true);
        Assert.Multiple(() =>
        {
            Assert.That(OligoExport.ReverseComplement(pair.Top), Is.EqualTo(pair.Bottom));
            Assert.That(OligoExport.ReverseComplement(pair.Bottom), Is.EqualTo(pair.Top));
            Assert.That(OligoExport.Template("ACGUACGUACGUACGUACGUACGUACGU", true, OligoStrand.Antisense), Is.EqualTo(pair.Bottom));
        });
    }

    [Test]
    public void ShortlistCsv_ListsRestrictionSitesOfTheOrderedDna()
    {
        var items = new[]
        {
            new ShortlistItem("NG_1:g.1A>T", false, "ACGUACGUACGUACGUACGUACGUACGU", "1", 1, 1),
            new ShortlistItem("NG_1:g.2A>T", false, "ACGUGAAUUCACGUACGUACGUGAAGAC", "1", 1, 1)
        };
        var lines = OligoExport.ShortlistCsv(items, false, false, PlateKind.Plate96)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lines[1], Does.EndWith(","), "clean guide leaves the column empty");
            Assert.That(lines[2], Does.EndWith(",BbsI;EcoRI"), "enzymes in list order");
        });
    }

    [Test]
    public void ShortlistCsv_AddressesWellsLikeThePoolingPlan()
    {
        var items = new[]
        {
            new ShortlistItem("NG_1:g.1A>T", false, "ACGUACGUACGUACGUACGUACGUACGU", "1", 1, 1),
            new ShortlistItem("NG_1:g.1A>T", true, "UGCAUGCAUGCAUGCAUGCAUGCAUGCA", "1", 1, 1)
        };
        var lines = OligoExport.ShortlistCsv(items, true, true, PlateKind.Plate96)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("Well,Name,Oligo,Sequence 5'-3',Length,Restriction sites"));
            Assert.That(lines, Has.Count.EqualTo(5), "header + 2 guides x 2 oligos");
            Assert.That(lines[1], Does.StartWith("Plate 1 - A1,NG_1:g.1A>T_top,top,"));
            Assert.That(lines[4], Does.StartWith("Plate 1 - A4,NG_1:g.1A>T_C_bottom,bottom,"));
            Assert.That(OligoExport.ShortlistCsv(items, false, false, PlateKind.Plate384).Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(3));
        });
    }
}
