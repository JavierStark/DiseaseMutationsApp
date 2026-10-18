using gRNA;
using NUnit.Framework;

namespace DiseaseMutationsAppTests;

/// <summary>The substitution rule's two positions (variant and engineered mismatch) are parameters, counted from the spacer's 3' end.</summary>
public class SpecialRulePositionTests
{
    private static SpacerFinder.gRNAResult Candidate(string sequence, int highlightStart)
    {
        var g = SpacerFinder.getgRNAResult(0, 3, sequence);
        return new SpacerFinder.gRNAResult(g.Sequence, g.GCScore, g.GCContent, g.HomopolymerCount, g.SeedRegion, 1,
            new RNAFoldWrapper.RNAFoldResult("....", -1.0), 2, 0.5, highlightStart, 1, -1, false, true, -1.0);
    }

    [Test]
    public void AdjustPositionFromEnd_FlipsTheRequestedLetter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.adjustPositionFromEndToAorU(1, "CCCCCC"), Is.EqualTo("CCCCCA"));
            Assert.That(SpacerFinder.adjustPositionFromEndToAorU(5, "CCCCCC"), Is.EqualTo("CACCCC"));
            Assert.That(SpacerFinder.adjustPositionFromEndToAorU(2, "CCCCAC"), Is.EqualTo("CCCCUC"), "an A becomes U");
            Assert.That(SpacerFinder.adjustPositionFromEndToAorU(9, "CCCC"), Is.EqualTo("CCCC"), "outside the sequence: unchanged");
            Assert.That(SpacerFinder.adjustPositionFromEndToAorU(0, "CCCC"), Is.EqualTo("CCCC"));
            Assert.That(SpacerFinder.adjustFourthFromEndToAorU("CCCCCC"), Is.EqualTo(SpacerFinder.adjustPositionFromEndToAorU(4, "CCCCCC")), "the default is the 4th");
        });
    }

    [Test]
    public void RuleAt_UsesTheGivenPositions()
    {
        // Variant 5th from the 3' end of a 10-letter spacer sits at index 5; the mismatch is placed 2nd from the end.
        var c = Candidate("GGGGGGGGGG", 5);
        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { c });
        var result = SpacerFinder.applySubstitutionSpecialRuleAt(5, 2, 0, 3, 10, input);
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.specialRuleAppliesAt(5, 10, input), Is.True);
            Assert.That(SpacerFinder.specialRuleAppliesAt(3, 10, input), Is.False, "the default position does not match this candidate");
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Engineered, Is.True);
            Assert.That(result[0].Sequence, Is.EqualTo("GGGGGGGGAG"), "the letter 2nd from the 3' end became A");
            Assert.That(result[1].Sequence, Is.EqualTo("GGGGGGGGGG"), "the ordinary candidate is kept");
        });
    }

    [Test]
    public void DefaultWrapper_EqualsTheRuleAt3And4()
    {
        var input = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { Candidate("UUUUGG", 3) });
        var a = SpacerFinder.applySubstitutionSpecialRule(0, 3, 6, input);
        var b = SpacerFinder.applySubstitutionSpecialRuleAt(3, 4, 0, 3, 6, input);
        Assert.That(a.Select(r => r.Sequence), Is.EqualTo(b.Select(r => r.Sequence)));
    }

    [TestCase(0, 4)]
    [TestCase(3, 0)]
    [TestCase(29, 4)]
    [TestCase(3, 29)]
    [TestCase(5, 5)]
    public void ValidateSpecialRule_RefusesPositionsOutsideTheSpacerOrEqual(int snp, int mm)
        => Assert.Throws<GrnaInputException>(() => Main.validateSpecialRule(28, new Main.SpecialRuleOptions(snp, mm)));

    [Test]
    public void ValidateSpecialRule_AcceptsTheDefaultAndOtherPositions()
    {
        Assert.DoesNotThrow(() => Main.validateSpecialRule(28, Main.defaultSpecialRule));
        Assert.DoesNotThrow(() => Main.validateSpecialRule(28, new Main.SpecialRuleOptions(26, 24)));
    }

    [Test]
    public void Notice_NamesThePositionAndTheSidesItNeeds()
    {
        var hgvs = new HGVS.HGVS("NG_000007.3:g.2A>T");
        var text = Main.specialRuleNoticeAt(5, hgvs, 100, 28, false, true).Value;
        Assert.That(text, Does.Contain("position 5").And.Contain("at least 4 and 23"));
    }

    [Test]
    public void Provenance_RecordsTheRule_AndItIsReadBack()
    {
        var line = ReportSchema.provenanceWithRule(5, 7, 28, 10, 17);
        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("variant at N-5, mismatch at N-7"));
            Assert.That(ReportSchema.tryParseRule(line).Value, Is.EqualTo(Tuple.Create(5, 7)));
            Assert.That(ReportSchema.tryParseProvenance(line).Value, Is.EqualTo(Tuple.Create(28, 10, 17)), "spacer and seed still parse");
            // A report made before the rule was recorded used the default.
            Assert.That(ReportSchema.tryParseRule("# Diana report; spacer=28; seed=10-17; generated=2026-10-01T00:00:00Z").Value, Is.EqualTo(Tuple.Create(3, 4)));
            Assert.That(Microsoft.FSharp.Core.FSharpOption<Tuple<int, int>>.get_IsNone(ReportSchema.tryParseRule("RS ID,HGVS")), Is.True);
        });
    }
}
