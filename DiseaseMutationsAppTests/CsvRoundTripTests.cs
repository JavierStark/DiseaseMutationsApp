using System.Text;
using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class CsvRoundTripTests
{
    private static GRNAResult Guide(string seq, int rank) => new()
    {
        Sequence = seq, SeedRegion = seq[..4], Rank = rank, Score = 0.9, GCContent = 50, GCScore = 1,
        Allignments = 1, HomopolymerCount = 0, RnaFoldResult = new RNAFoldResult { Structure = "....", Energy = -1.5 }
    };

    [Test]
    public void ExportedHeader_IsRecognisedByParser()
    {
        Assert.That(GuideListParser.LooksLikeBuilderCsv(GrnaCsvSchema.Header), Is.True);
    }

    [Test]
    public void ExportedRow_HasAsManyColumnsAsHeader()
    {
        var row = GrnaCsvSchema.Row("123", "NM_000546.6:c.215C>G", GrnaCsvSchema.MutatedType, Guide("ACGUACGU", 1), false);
        Assert.That(row.Split(',').Length, Is.EqualTo(GrnaCsvSchema.Columns.Length));
    }

    [Test]
    public void Export_ThenParse_RoundTripsBestMutatedGuide()
    {
        var sb = new StringBuilder();
        sb.AppendLine(GrnaCsvSchema.Header);
        GrnaCsvSchema.AppendRows(sb, "123", "NM_1:c.1A>G", new[] { Guide("AAAACCCC", 2), Guide("GGGGUUUU", 1) }, GrnaCsvSchema.MutatedType, false);
        GrnaCsvSchema.AppendRows(sb, "123", "NM_1:c.1A>G", new[] { Guide("CCCCAAAA", 1) }, GrnaCsvSchema.OriginalType, false);

        var parsed = GuideListParser.Parse(sb.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Source, Is.EqualTo(GuideListSource.BuilderCsv));
            Assert.That(parsed.Guides, Has.Count.EqualTo(1));
            Assert.That(parsed.Guides[0].Sequence, Is.EqualTo("GGGGUUUU"));
            Assert.That(parsed.Guides[0].RsId, Is.EqualTo("123"));
        });
    }

    [Test]
    public void Parse_BothStrandsOfSameHgvs_KeepsFirstStrandAndWarns()
    {
        var sb = new StringBuilder();
        sb.AppendLine(GrnaCsvSchema.Header);
        GrnaCsvSchema.AppendRows(sb, "1", "NM_1:c.1A>G", new[] { Guide("AAAACCCC", 3) }, GrnaCsvSchema.MutatedType, false);
        GrnaCsvSchema.AppendRows(sb, "1", "NM_1:c.1A>G", new[] { Guide("GGGGUUUU", 1) }, GrnaCsvSchema.MutatedType, true);

        var parsed = GuideListParser.Parse(sb.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Guides, Has.Count.EqualTo(1));
            Assert.That(parsed.Guides[0].Sequence, Is.EqualTo("AAAACCCC"));
            Assert.That(parsed.Warnings, Has.Some.Contains("different strand"));
        });
    }

    [Test]
    public void Csv_QuotesFieldsWithCommasAndQuotes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GrnaCsvSchema.Csv("plain"), Is.EqualTo("plain"));
            Assert.That(GrnaCsvSchema.Csv("a,b"), Is.EqualTo("\"a,b\""));
            Assert.That(GrnaCsvSchema.Csv("say \"hi\""), Is.EqualTo("\"say \"\"hi\"\"\""));
        });
    }

    [Test]
    public void FornaUrl_IsPlainHttpWithRawUrlSafeValues()
    {
        var svc = new GrnaService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService());
        var url = svc.GetFornaUrl("GAUUUAGACU", "..((..))..");
        // FORNA does not percent-decode its query values, so brackets must not be encoded.
        Assert.That(url, Is.EqualTo("http://nibiru.tbi.univie.ac.at/forna/forna.html?id=url/name&sequence=GAUUUAGACU&structure=..((..)).."));
    }

    [TestCase("GA CU", "..")]
    [TestCase("GACU", "..&x=1")]
    [TestCase("GACU&evil=1", "..")]
    [TestCase("", "..")]
    public void FornaUrl_RejectsAnythingThatCouldInjectQueryParameters(string sequence, string structure)
    {
        var svc = new GrnaService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrnaService>.Instance, new gRNA.Services.BowtieService());
        Assert.Throws<ArgumentException>(() => svc.GetFornaUrl(sequence, structure));
    }
}
