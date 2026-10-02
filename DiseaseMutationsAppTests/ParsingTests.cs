using System.Linq;
using gRNA;

namespace DiseaseMutationsAppTests;

public class RnaFoldParsingTests
{
    [TestCase("..((..))..\t-2.09", "..((..))..", -2.09)]
    [TestCase("['..((..))..', -2.09]", "..((..))..", -2.09)]
    [TestCase("('..((..))..', -2.09)", "..((..))..", -2.09)]
    [TestCase("(\"..((..))..\", -2.09)", "..((..))..", -2.09)]
    [TestCase("  ...  \t0.0  ", "...", 0.0)]
    public void ParseFoldLine_AcceptsAllKnownFormats(string line, string structure, double energy)
    {
        var (s, e) = Parsing.parseFoldLine(line);
        Assert.Multiple(() =>
        {
            Assert.That(s, Is.EqualTo(structure));
            Assert.That(e, Is.EqualTo(energy).Within(1e-9));
        });
    }

    [TestCase("garbage")]
    [TestCase("....\tnotanumber")]
    [TestCase("['....', abc]")]
    public void ParseFoldLine_BadInput_ThrowsTypedUpstreamException(string line)
    {
        Assert.Throws<GrnaUpstreamException>(() => Parsing.parseFoldLine(line));
    }

    [Test]
    public void ParseFoldBatch_ParsesOnePerLine_IgnoringBlankLines()
    {
        var result = Parsing.parseFoldBatch("..\t-1.5\r\n\r\n((..))\t-3.25\n");
        Assert.That(result.Select(r => r.Item2), Is.EqualTo(new[] { -1.5, -3.25 }));
    }
}

public class BowtieOutputParsingTests
{
    private const string Output =
        "0\t+\tchr7\t147119362\tACTGACTGACTG\tIIIIIIIIIIII\t478\t\n" +
        "0\t-\tchr9\t37515365\tACTGACTGACTG\tIIIIIIIIIIII\t478\t\n" +
        "2\t+\tchr1\t10\tTTTT\tIIII\t0\t\n" +
        "\n# reads processed: 3\n# Reported 3 alignments\n";

    [Test]
    public void ParseBowtieAlignments_ParsesRecords_AndSkipsSummary()
    {
        var parsed = Parsing.parseBowtieAlignments(Output).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Has.Count.EqualTo(3));
            Assert.That(parsed[1].Strand, Is.EqualTo('-'));
            Assert.That(parsed[1].Reference, Is.EqualTo("chr9"));
            Assert.That(parsed[1].Offset, Is.EqualTo(37515365L));
        });
    }

    [Test]
    public void CountAlignments_ReturnsOneCountPerRead_ZeroForUnaligned()
    {
        var parsed = Parsing.parseBowtieAlignments(Output);
        var counts = Parsing.countAlignments(4, parsed);
        Assert.That(counts.ToArray(), Is.EqualTo(new[] { 2, 0, 1, 0 }));
    }

    [Test]
    public void TryParseBowtieLine_MalformedLine_IsNone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Parsing.tryParseBowtieLine("not\ta\tvalid"), Is.Null);
            Assert.That(Parsing.tryParseBowtieLine("x\t+\tchr1\t1\tAAA"), Is.Null);
        });
    }

    [TestCase("genome.1.bt2", "genome")]
    [TestCase("genome.rev.2.bt2l", "genome")]
    [TestCase("GRCh38_noalt_as.rev.1.ebwt", "GRCh38_noalt_as")]
    public void StripIndexSuffix_RecognisesBowtie1And2(string file, string expected)
    {
        Assert.That(BowtieWrapper.tryStripIndexSuffix(file).Value, Is.EqualTo(expected));
    }
}
