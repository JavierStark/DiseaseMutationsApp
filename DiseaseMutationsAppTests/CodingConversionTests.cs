using gRNA;
using NUnit.Framework;

namespace DiseaseMutationsAppTests;

/// <summary>c. (coding-sequence) positions on a transcript record are converted to positions of the mRNA.</summary>
public class CodingConversionTests
{
    // HBB mRNA NM_000518.5: the coding sequence runs from 51 to 494 (the sickle-cell A>T is c.20, so n.70).
    private const int CdsStart = 51, CdsEnd = 494;

    [TestCase("NM_000518.5:c.20A>T", "NM_000518.5:n.70A>T")]
    [TestCase("NM_000518.5:c.1A>G", "NM_000518.5:n.51A>G")]
    [TestCase("NM_000518.5:c.-5G>A", "NM_000518.5:n.46G>A")]
    [TestCase("NM_000518.5:c.*3C>T", "NM_000518.5:n.497C>T")]
    [TestCase("NM_000518.5:c.20_22del", "NM_000518.5:n.70_72del")]
    [TestCase("NM_000518.5:c.20_21insA", "NM_000518.5:n.70_71insA")]
    [TestCase("NM_000518.5:g.70A>T", "NM_000518.5:g.70A>T")]   // not a c. notation: unchanged
    public void Converts(string code, string expected)
        => Assert.That(HGVS.convertCodingToTranscript(CdsStart, CdsEnd, code), Is.EqualTo(expected));

    [Test]
    public void TryParseCoding_FindsTheAccessionAndTheBody()
    {
        var parsed = HGVS.tryParseCoding("NM_000518.5:c.20A>T");
        Assert.Multiple(() =>
        {
            Assert.That(Microsoft.FSharp.Core.FSharpOption<Tuple<string, string>>.get_IsSome(parsed), Is.True);
            Assert.That(parsed.Value.Item1, Is.EqualTo("NM_000518.5"));
            Assert.That(parsed.Value.Item2, Is.EqualTo("20A>T"));
        });
    }

    [TestCase("NG_000007.3:c.20A>T")]
    [TestCase("NC_000011.10:c.20A>T")]
    public void RefusesAGenomicRecord_WithGuidance(string code)
    {
        var ex = Assert.Throws<GrnaInputException>(() => HGVS.convertCodingToTranscript(CdsStart, CdsEnd, code));
        Assert.That(ex!.Message, Does.Contain("NM_").And.Contain("g."));
    }

    [Test]
    public void RefusesAnIntronicOffset()
    {
        var ex = Assert.Throws<GrnaInputException>(() => HGVS.convertCodingToTranscript(CdsStart, CdsEnd, "NM_000518.5:c.92+2T>C"));
        Assert.That(ex!.Message, Does.Contain("intronic"));
    }

    [Test]
    public void RefusesPositionZeroAndPositionsBeforeTheTranscript()
    {
        Assert.Throws<GrnaInputException>(() => HGVS.convertCodingToTranscript(CdsStart, CdsEnd, "NM_000518.5:c.0A>T"));
        Assert.Throws<GrnaInputException>(() => HGVS.convertCodingToTranscript(CdsStart, CdsEnd, "NM_000518.5:c.-60A>T"));
    }

    [Test]
    public void ParseCdsRange_ReadsASingleRange()
    {
        var gb = "LOCUS       NM_000518   626 bp\n     source          1..626\n     gene            1..626\n     CDS             51..494\n                     /gene=\"HBB\"\n";
        Assert.That(SequenceRepository.parseCdsRange("NM_000518.5", gb), Is.EqualTo(Tuple.Create(51, 494)));
    }

    [Test]
    public void ParseCdsRange_RefusesASplitCdsAndARecordWithoutCds()
    {
        Assert.Throws<GrnaInputException>(() => SequenceRepository.parseCdsRange("X", "     CDS             join(1..10,20..30)\n"));
        Assert.Throws<GrnaInputException>(() => SequenceRepository.parseCdsRange("X", "     gene            1..626\n"));
    }

    [Test]
    public void Design_OfCodingNotationOnAGenomicRecord_FailsAsBadInputBeforeAnyRequest()
    {
        // requireCodingTranscript runs before the network, so this needs no mock.
        var task = Main.getBestgRNAFromHGVS("NG_000007.3:c.20A>T", 28, 10, 17, new gRNA.Services.BowtieService(), CancellationToken.None, false);
        Assert.ThrowsAsync<GrnaInputException>(async () => await task);
    }
}

public class ScaffoldChecksTests
{
    [Test]
    public void BasePairs_ReadsDotBracket()
        => Assert.That(SpacerFinder.basePairs("((..))").OrderBy(p => p.Item1).Select(p => (p.Item1, p.Item2)),
            Is.EqualTo(new[] { (0, 5), (1, 4) }));

    [Test]
    public void ScaffoldIntact_WhenEveryReferencePairSurvives()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.scaffoldIntact("((..))....", "((..))..()"), Is.True, "extra pairing in the spacer part is allowed");
            Assert.That(SpacerFinder.scaffoldIntact("((..))....", "(......).."), Is.False, "the hairpin is gone");
            Assert.That(SpacerFinder.scaffoldIntact("", "((..))"), Is.True, "no reference: nothing to compare");
        });
    }

    [Test]
    public void SpacerUnpaired_IsTheShareOfDotsAfterTheScaffold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.spacerUnpaired(4, "((..)).."), Is.EqualTo(0.5));
            Assert.That(SpacerFinder.spacerUnpaired(4, "...."), Is.EqualTo(-1.0), "no spacer part");
            Assert.That(SpacerFinder.spacerUnpaired(4, ""), Is.EqualTo(-1.0));
        });
    }
}
