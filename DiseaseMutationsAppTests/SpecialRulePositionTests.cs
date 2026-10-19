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

    private static SpacerFinder.SpecialRuleOptions Rule(int snp, int mm, int count = 1, string bases = SpacerFinder.DefaultEngineeredBases)
        => new(snp, mm, count, bases, true);

    private static Microsoft.FSharp.Collections.FSharpList<SpacerFinder.gRNAResult> One(SpacerFinder.gRNAResult c)
        => Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { c });

    [Test]
    public void Defaults_FollowTheLabSpec()
    {
        var d = SpacerFinder.defaultSpecialRule;
        Assert.Multiple(() =>
        {
            Assert.That(d.SnpPos, Is.EqualTo(9));
            Assert.That(d.MismatchPos, Is.EqualTo(10));
            Assert.That(d.MismatchCount, Is.EqualTo(2));
            Assert.That(SpacerFinder.mismatchPositions(d), Is.EqualTo(new[] { 10, 11 }.ToList()));
            Assert.That(SpacerFinder.mismatchPositions(Rule(9, 10, 1)), Is.EqualTo(new[] { 10 }.ToList()), "with one mutation the single position is 10");
            Assert.That(SpacerFinder.DefaultSpacerSize, Is.EqualTo(23));
            Assert.That(SpacerFinder.DefaultEngineeredBases, Is.EqualTo("A>A,G>A,C>C,U>C"));
        });
    }

    [Test]
    public void EngineeredBases_DefaultTableMapsTheTargetBaseToTheGuideBase()
    {
        var table = SpacerFinder.parseEngineeredBases(SpacerFinder.DefaultEngineeredBases);
        // The spacer letter pairs with the target ("sequence") base: spacer U pairs with target A (A->A), spacer A with U (U->C),
        // spacer C with G (G->A), spacer G with C (C->C).
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.engineeredLetter(table, 'U'), Is.EqualTo('A'));
            Assert.That(SpacerFinder.engineeredLetter(table, 'A'), Is.EqualTo('C'));
            Assert.That(SpacerFinder.engineeredLetter(table, 'C'), Is.EqualTo('A'));
            Assert.That(SpacerFinder.engineeredLetter(table, 'G'), Is.EqualTo('C'));
            Assert.That(SpacerFinder.engineeredLetter(table, 'N'), Is.EqualTo('N'), "not a base: unchanged");
        });
    }

    [Test]
    public void EngineeredBases_AnEditedRowChangesTheGuideBase()
    {
        var table = SpacerFinder.parseEngineeredBases("A>G,G>A,C>C,U>C");
        Assert.That(SpacerFinder.engineeredLetter(table, 'U'), Is.EqualTo('G'), "target A now pairs with guide G");
    }

    [TestCase("A>A,G>A,C>C")]
    [TestCase("A>A,A>C,C>C,U>C")]
    [TestCase("A>A,G>A,C>C,U>X")]
    [TestCase("A>AA,G>A,C>C,U>C")]
    [TestCase("")]
    public void EngineeredBases_RefusesATableThatIsNotOneRowPerBase(string text)
        => Assert.Throws<ArgumentException>(() => SpacerFinder.parseEngineeredBases(text));

    [Test]
    public void EngineerPositionFromEnd_ReplacesTheRequestedLetter()
    {
        var table = SpacerFinder.parseEngineeredBases(SpacerFinder.DefaultEngineeredBases);
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.engineerPositionFromEnd(table, 1, "GGGGGG"), Is.EqualTo("GGGGGC"));
            Assert.That(SpacerFinder.engineerPositionFromEnd(table, 5, "GGGGGG"), Is.EqualTo("GCGGGG"));
            Assert.That(SpacerFinder.engineerPositionFromEnd(table, 9, "CCCC"), Is.EqualTo("CCCC"), "outside the sequence: unchanged");
            Assert.That(SpacerFinder.engineerPositionFromEnd(table, 0, "CCCC"), Is.EqualTo("CCCC"));
        });
    }

    [Test]
    public void RuleWith_UsesTheGivenPositions()
    {
        // Variant 5th from the 3' end of a 10-letter spacer sits at index 5; the mismatch is placed 2nd from the end.
        var input = One(Candidate("GGGGGGGGGG", 5));
        var result = SpacerFinder.applySubstitutionSpecialRuleWith(Rule(5, 2), 0, 3, 10, input);
        Assert.Multiple(() =>
        {
            Assert.That(SpacerFinder.specialRuleAppliesAt(5, 10, input), Is.True);
            Assert.That(SpacerFinder.specialRuleAppliesAt(3, 10, input), Is.False, "another position does not match this candidate");
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Engineered, Is.True);
            Assert.That(result[0].Sequence, Is.EqualTo("GGGGGGGGCG"), "the G 2nd from the 3' end pairs with C, whose guide base is C");
            Assert.That(result[1].Sequence, Is.EqualTo("GGGGGGGGGG"), "the ordinary candidate is kept");
        });
    }

    [Test]
    public void RuleWith_TwoMutationsChangeThePositionAndTheNextOne()
    {
        var result = SpacerFinder.applySubstitutionSpecialRuleWith(Rule(5, 2, 2), 0, 3, 10, One(Candidate("GGGGGGGGGG", 5)));
        Assert.That(result[0].Sequence, Is.EqualTo("GGGGGGGCCG"), "the 2nd and 3rd letters from the 3' end");
    }

    [Test]
    public void DefaultRule_On23nt_ChangesPositions10And11_AndKeepsTheVariantAt9()
    {
        var c = Candidate(new string('A', 23), 23 - 9);
        var two = SpacerFinder.applySubstitutionSpecialRule(0, 3, 23, One(c));
        var one = SpacerFinder.applySubstitutionSpecialRuleWith(new SpacerFinder.SpecialRuleOptions(9, 10, 1, SpacerFinder.DefaultEngineeredBases, true), 0, 3, 23, One(c));
        Assert.Multiple(() =>
        {
            Assert.That(two[0].Sequence, Is.EqualTo(new string('A', 12) + "CC" + new string('A', 9)), "positions 10 and 11 from the 3' end (A pairs with U, whose guide base is C)");
            Assert.That(one[0].Sequence, Is.EqualTo(new string('A', 13) + "C" + new string('A', 9)), "only position 10");
            Assert.That(two[0].MutationHighlightStart, Is.EqualTo(14), "the variant letter (9th from the end) is untouched");
        });
    }

    [Test]
    public void AnEditedTable_ChangesWhatTheRuleWrites()
    {
        var result = SpacerFinder.applySubstitutionSpecialRuleWith(Rule(5, 2, 1, "A>A,G>A,C>C,U>G"), 0, 3, 10, One(Candidate("AAAAAAAAAA", 5)));
        Assert.That(result[0].Sequence, Is.EqualTo("AAAAAAAAGA"), "spacer A pairs with target U, now mapped to G");
    }

    [TestCase(0, 4, 1)]
    [TestCase(3, 0, 1)]
    [TestCase(29, 4, 1)]
    [TestCase(3, 29, 1)]
    [TestCase(5, 5, 1)]
    [TestCase(5, 4, 2)]   // 4 and 5: the second one lands on the variant
    [TestCase(3, 28, 2)]  // 28 and 29: the second one is outside the spacer
    [TestCase(3, 4, 3)]
    [TestCase(3, 4, 0)]
    public void ValidateSpecialRule_RefusesPositionsOutsideTheSpacerOrOnTheVariant(int snp, int mm, int count)
        => Assert.Throws<GrnaInputException>(() => Main.validateSpecialRule(28, Rule(snp, mm, count)));

    [Test]
    public void ValidateSpecialRule_RefusesABrokenTable()
        => Assert.Throws<GrnaInputException>(() => Main.validateSpecialRule(28, Rule(9, 10, 2, "A>A,G>A,C>C")));

    [Test]
    public void ValidateSpecialRule_AcceptsTheDefaultAndOtherPositions()
    {
        Assert.DoesNotThrow(() => Main.validateSpecialRule(23, Main.defaultSpecialRule));
        Assert.DoesNotThrow(() => Main.validateSpecialRule(28, Rule(26, 24, 2)));
        Assert.DoesNotThrow(() => Main.validateSpecialRule(23, Rule(9, 10, 1)));
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
        var rule = Rule(5, 7, 2, "A>G,G>A,C>C,U>C");
        var line = ReportSchema.provenanceWithRule(rule, 28, 10, 17);
        var parsed = ReportSchema.tryParseRule(line).Value;
        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("variant at N-5, mismatch at N-7 and N-8"));
            Assert.That(ReportSchema.sameRule(parsed, rule), Is.True);
            Assert.That(parsed.MismatchCount, Is.EqualTo(2));
            Assert.That(ReportSchema.tryParseProvenance(line).Value, Is.EqualTo(Tuple.Create(28, 10, 17)), "spacer and seed still parse");
            Assert.That(ReportSchema.sameRule(ReportSchema.tryParseRule(ReportSchema.provenance(23, 10, 17)).Value, Main.defaultSpecialRule), Is.True);
            Assert.That(ReportSchema.sameRule(parsed, Main.defaultSpecialRule), Is.False);
            // A report made before the rule was recorded used variant 3, one mismatch at 4 and flipped the letter: never equal to a current rule.
            var old = ReportSchema.tryParseRule("# Diana report; spacer=28; seed=10-17; generated=2026-10-01T00:00:00Z").Value;
            Assert.That(old.SnpPos, Is.EqualTo(3));
            Assert.That(old.MismatchPos, Is.EqualTo(4));
            Assert.That(ReportSchema.sameRule(old, Main.defaultSpecialRule), Is.False);
            Assert.That(Microsoft.FSharp.Core.FSharpOption<SpacerFinder.SpecialRuleOptions>.get_IsNone(ReportSchema.tryParseRule("RS ID,HGVS")), Is.True);
        });
    }

    [Test]
    public void OptOut_SkipsValidation_AndIsRecordedAsOff()
    {
        var off = new SpacerFinder.SpecialRuleOptions(99, 10, 2, SpacerFinder.DefaultEngineeredBases, false);
        Assert.DoesNotThrow(() => Main.validateSpecialRule(23, off), "a rule that is switched off is not validated");
        var line = ReportSchema.provenanceWithRule(off, 23, 10, 17);
        var parsed = ReportSchema.tryParseRule(line).Value;
        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("special-rule=off"));
            Assert.That(parsed.Enabled, Is.False);
            Assert.That(ReportSchema.sameRule(parsed, new SpacerFinder.SpecialRuleOptions(5, 10, 2, SpacerFinder.DefaultEngineeredBases, false)), Is.True, "off equals off");
            Assert.That(ReportSchema.sameRule(parsed, SpacerFinder.defaultSpecialRule), Is.False);
        });
    }
}
