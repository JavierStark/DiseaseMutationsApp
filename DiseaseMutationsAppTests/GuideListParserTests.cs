using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class GuideListParserTests
{
    /// <summary>Mirrors the header written by Index.razor.cs DownloadReport.</summary>
    private const string BuilderHeader =
        "RS ID,HGVS,Sequence Type,Rank,Sequence,Score,GC Content,Alignments,Seed Region,Homopolymers,Fold Energy";

    [Test]
    public void Parse_NullOrWhitespace_ReturnsEmptyList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GuideListParser.Parse(null).Guides, Is.Empty);
            Assert.That(GuideListParser.Parse("   \r\n  ").Guides, Is.Empty);
        });
    }

    [Test]
    public void Parse_NewlineSeparatedList_KeepsOrder()
    {
        var result = GuideListParser.Parse("guide-A\nguide-B\nguide-C");

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(GuideListSource.PlainList));
            Assert.That(result.Guides.Select(g => g.Label), Is.EqualTo(new[] { "guide-A", "guide-B", "guide-C" }));
            Assert.That(result.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void Parse_CommaSeparatedList_SplitsAndTrims()
    {
        var result = GuideListParser.Parse("guide-A, guide-B ,guide-C");

        Assert.That(result.Guides.Select(g => g.Label), Is.EqualTo(new[] { "guide-A", "guide-B", "guide-C" }));
    }

    [Test]
    public void Parse_PlainList_IgnoresBlankLinesAndCrLf()
    {
        var result = GuideListParser.Parse("guide-A\r\n\r\n  \r\nguide-B\r\n");

        Assert.That(result.Guides.Select(g => g.Label), Is.EqualTo(new[] { "guide-A", "guide-B" }));
    }

    [Test]
    public void Parse_PlainList_DropsDuplicatesAndWarns()
    {
        var result = GuideListParser.Parse("guide-A\nguide-B\nGUIDE-A");

        Assert.Multiple(() =>
        {
            Assert.That(result.Guides.Select(g => g.Label), Is.EqualTo(new[] { "guide-A", "guide-B" }));
            Assert.That(result.Warnings, Has.Some.Contains("duplicate"));
        });
    }

    /// <summary>A plain list must not be misread as a CSV just because it contains commas.</summary>
    [Test]
    public void Parse_PlainListWithoutHeader_IsNotTreatedAsCsv()
    {
        var result = GuideListParser.Parse("NM_000546.6:c.215C>G\nNM_000546.6:c.217C>T");

        Assert.That(result.Source, Is.EqualTo(GuideListSource.PlainList));
    }

    [Test]
    public void Parse_BuilderCsv_KeepsBestRankedMutatedSpacerPerVariant()
    {
        var csv = string.Join("\n",
            BuilderHeader,
            "12345,NM_000546.6:c.215C>G,Mutated,2,AAAAUUUUGGGGCCCCAAAAUUUUGGGG,0.8,50,1,UUUUGGGG,0,-3.2",
            "12345,NM_000546.6:c.215C>G,Mutated,1,GGGGCCCCAAAAUUUUGGGGCCCCAAAA,0.9,50,0,GGGGCCCC,0,-4.1",
            "12345,NM_000546.6:c.215C>G,Original,1,CCCCAAAAUUUUGGGGCCCCAAAAUUUU,0.7,50,0,CCCCAAAA,0,-2.0",
            "12345,NM_000546.6:c.217C>T,Mutated,1,UUUUGGGGCCCCAAAAUUUUGGGGCCCC,0.85,50,0,UUUUGGGG,0,-3.9");

        var result = GuideListParser.Parse(csv);

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(GuideListSource.BuilderCsv));
            Assert.That(result.Count, Is.EqualTo(2));

            Assert.That(result.Guides[0].Label, Is.EqualTo("NM_000546.6:c.215C>G"));
            Assert.That(result.Guides[0].Sequence, Is.EqualTo("GGGGCCCCAAAAUUUUGGGGCCCCAAAA"));
            Assert.That(result.Guides[0].RsId, Is.EqualTo("12345"));

            Assert.That(result.Guides[1].Label, Is.EqualTo("NM_000546.6:c.217C>T"));
        });
    }

    [Test]
    public void Parse_BuilderCsv_WarnsAboutSkippedOriginalRows()
    {
        var csv = string.Join("\n",
            BuilderHeader,
            "12345,NM_000546.6:c.215C>G,Mutated,1,AAAA,0.9,50,0,AAAA,0,-4.1",
            "12345,NM_000546.6:c.215C>G,Original,1,CCCC,0.7,50,0,CCCC,0,-2.0");

        var result = GuideListParser.Parse(csv);

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Some.Contains("original-sequence"));
        });
    }

    [Test]
    public void Parse_BuilderCsv_SkipsMalformedRowsAndWarns()
    {
        var csv = string.Join("\n",
            BuilderHeader,
            "12345,NM_000546.6:c.215C>G,Mutated,1,AAAA,0.9,50,0,AAAA,0,-4.1",
            "not,enough,columns",
            ",,Mutated,1,GGGG,0.9,50,0,GGGG,0,-4.1");

        var result = GuideListParser.Parse(csv);

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Warnings, Has.Some.Contains("expected columns"));
        });
    }

    [Test]
    public void Parse_BuilderCsv_HandlesCrLfAndTrailingNewline()
    {
        var csv = BuilderHeader + "\r\n12345,NM_000546.6:c.215C>G,Mutated,1,AAAA,0.9,50,0,AAAA,0,-4.1\r\n";

        var result = GuideListParser.Parse(csv);

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(GuideListSource.BuilderCsv));
            Assert.That(result.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Parse_BuilderCsvWithOnlyHeader_ReturnsEmptyList()
    {
        var result = GuideListParser.Parse(BuilderHeader);

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(GuideListSource.BuilderCsv));
            Assert.That(result.Guides, Is.Empty);
        });
    }

    [Test]
    public void LooksLikeBuilderCsv_DetectsHeaderCaseInsensitively()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GuideListParser.LooksLikeBuilderCsv(BuilderHeader), Is.True);
            Assert.That(GuideListParser.LooksLikeBuilderCsv("rs id,hgvs,sequence type,rank"), Is.True);
            Assert.That(GuideListParser.LooksLikeBuilderCsv("guide-A\nguide-B"), Is.False);
            Assert.That(GuideListParser.LooksLikeBuilderCsv(null), Is.False);
        });
    }
}
