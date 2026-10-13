using DiseaseMutationsApp.Services;
using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Phase 0 of the project review (docs/review-findings.md). Each test states the DESIRED behaviour for a defect
/// that was confirmed by a real run or by reading the code. While the defect is open the test carries
/// [Explicit] with its finding id; the fix removes the attribute. The tests are run once without it to prove they fail.
/// </summary>
public class KnownDefectTests
{
    // H4: an HGVS inversion is the REVERSE COMPLEMENT of the segment (den Dunnen et al. 2016), not just the reverse.
    [Test]
    public void H4_Inversion_IsReverseComplemented()
    {
        var sequence = new Sequence.Sequence("NG_016465.4", "ATGCGTACGTAGCTAGCTAGCTAGCTAGCTAGCTAGCTAGC");
        var hgvs = new HGVS.HGVS("NG_016465.4:c.5_7inv");   // GTA -> TAC, flanked by C (pos 4) and C (pos 8)
        var (mutated, _) = sequence.GetMutatedSubsequence(hgvs, 1, 1);
        Assert.That(mutated, Is.EqualTo("CTACC"));
    }

    // H7: malformed input must surface as GrnaInputException (CLI exit 2), never a raw IndexOutOfRangeException.
    [TestCase("NC_000017.11")]
    [TestCase("NC_000017.11:")]
    [TestCase("")]
    [TestCase("rs")]

    public void H7_MalformedHgvs_ThrowsTypedInputException(string code)
    {
        Assert.Throws<GrnaInputException>(() => _ = new HGVS.HGVS(code));
    }

    // H7: an oversized tube number in the decode box must not throw OverflowException (it would kill the circuit).
    [Test]
    public void H7_Decode_HugeNumbers_AreReportedAsUnrecognised()
    {
        var svc = new PoolingService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PoolingService>.Instance);
        var plan = svc.BuildPlan(PoolingModelKind.TwoDMatrix, 25, 5, PlateKind.Plate96);

        var outcome = svc.Decode(plan, "#99999999999, A99999999999");

        Assert.That(outcome.Unrecognised, Has.Count.EqualTo(2));
    }

    // H8: a spreadsheet opens a field that starts with = + - @ as a formula. Exports must neutralise it.
    [TestCase("=HYPERLINK(\"http://x\",\"y\")")]
    [TestCase("+1+1")]
    [TestCase("-2+3")]
    [TestCase("@SUM(A1)")]

    public void H8_Csv_NeutralisesFormulaCharacters(string value)
    {
        var field = GrnaCsvSchema.Csv(value);
        Assert.That(field.TrimStart('"'), Does.Not.StartWith(value[0].ToString()));
    }

    // H2 (characterisation, not a defect test): a read with no Bowtie hit is counted as 0 alignments, which
    // sorts ahead of the ideal 1. Pinned so a deliberate change of the rule shows up here. The intended
    // meaning of 0 is an open decision (see the plan, open items).
    [Test]
    public void H2_CountAlignments_NoHit_IsZero()
    {
        var counts = Parsing.countAlignments(2, Microsoft.FSharp.Collections.FSharpList<Parsing.BowtieAlignment>.Empty);
        Assert.That(System.Linq.Enumerable.ToArray(counts), Is.EqualTo(new[] { 0, 0 }));
    }
}
