using DiseaseMutationsApp.Services;
using gRNA;

namespace DiseaseMutationsAppTests;

public class ReportSchemaTests
{
    [TestCase("=1+1", "'=1+1")]
    [TestCase("+SUM(A1)", "'+SUM(A1)")]
    [TestCase("-5", "'-5")]
    [TestCase("@A1", "'@A1")]
    [TestCase("\tTAB", "'\tTAB")]
    [TestCase("NC_000017.11:g.7674220C>T", "NC_000017.11:g.7674220C>T")]
    [TestCase("", "")]
    [TestCase("a=b", "a=b")]    // only a LEADING formula character matters
    public void Neutralise_PrefixesOnlyLeadingFormulaCharacters(string input, string expected)
    {
        Assert.That(ReportSchema.neutralise(input), Is.EqualTo(expected));
    }

    [Test]
    public void CsvField_NeutralisesThenQuotes()
    {
        Assert.That(ReportSchema.csvField("=A,B"), Is.EqualTo("\"'=A,B\""));
    }

    [Test]
    public void CsvField_AndTheCSharpWriter_AreTheSameFunction()
    {
        foreach (var value in new[] { "plain", "with,comma", "with \"quote\"", "=cmd", "line\nbreak" })
            Assert.That(GrnaCsvSchema.Csv(value), Is.EqualTo(ReportSchema.csvField(value)), value);
    }

    [TestCase("a,b,c", new[] { "a", "b", "c" })]
    [TestCase("a,,c", new[] { "a", "", "c" })]
    [TestCase("\"a,b\",c", new[] { "a,b", "c" })]
    [TestCase("\"say \"\"hi\"\"\",x", new[] { "say \"hi\"", "x" })]
    [TestCase("", new[] { "" })]
    [TestCase("a,", new[] { "a", "" })]
    public void SplitCsvLine_HonoursQuotes(string line, string[] expected)
    {
        Assert.That(ReportSchema.splitCsvLine(line).ToArray(), Is.EqualTo(expected));
    }

    [TestCase("a,b")]
    [TestCase("quote \" inside")]
    [TestCase("x,\"y\",z")]
    [TestCase("=HYPERLINK(\"u\",\"v\")")]
    public void WriteThenSplit_RoundTrips_UpToTheNeutralisingPrefix(string value)
    {
        var line = string.Join(",", ReportSchema.csvField(value), ReportSchema.csvField("tail"));
        var fields = ReportSchema.splitCsvLine(line).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(fields, Has.Length.EqualTo(2));
            Assert.That(fields[0], Is.EqualTo(ReportSchema.neutralise(value)));
            Assert.That(fields[1], Is.EqualTo("tail"));
        });
    }

    [Test]
    public void Provenance_WrittenByTheLibrary_IsReadBack()
    {
        var line = ReportSchema.provenance(30, 12, 19);
        var parsed = ReportSchema.tryParseProvenance(line);
        Assert.That(parsed.Value, Is.EqualTo(Tuple.Create(30, 12, 19)));
    }

    [TestCase("")]
    [TestCase("RS ID,HGVS")]
    [TestCase("# some other comment")]
    [TestCase("# Diana report; spacer=x; seed=1-2")]
    public void Provenance_OfOtherLines_IsNone(string line)
    {
        Assert.That(ReportSchema.tryParseProvenance(line), Is.Null);
    }

    [Test]
    public void Provenance_WrittenByTheWebExport_IsReadBackByTheLibrary()
    {
        var parsed = ReportSchema.tryParseProvenance(GrnaCsvSchema.ProvenanceLine(28, 10, 17));
        Assert.That(parsed.Value, Is.EqualTo(Tuple.Create(28, 10, 17)));
    }
}
