using System.Globalization;
using System.Text;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// Single source of truth for the Diana CSV report: the web export writes it,
/// <see cref="GuideListParser"/> reads it, and the tests assert they agree.
/// </summary>
public static class GrnaCsvSchema
{
    /// <summary>Column names come from the F# library so the CLI and the web export share one definition.</summary>
    public static readonly string[] Columns = gRNA.ReportSchema.columns.ToArray();

    // Column positions.
    public const int ColRsId = 0;
    public const int ColHgvs = 1;
    public const int ColSequenceType = 2;
    public const int ColRank = 3;
    public const int ColSequence = 4;
    public const int ColStrand = 11;

    /// <summary>Columns that must be present for a header to be recognised (Strand is optional for older exports).</summary>
    public const int RequiredLeadingColumns = 3;
    public const int MinDataColumns = 5;

    public const string MutatedType = "Mutated";
    public const string OriginalType = "Original";
    public const string NormalStrand = "Normal";
    public const string ComplementStrand = "Complement";

    /// <summary>A leading comment line recording the parameters, so an exported report can be reproduced.</summary>
    /// <param name="snpPos">Where the substitution rule puts the variant, counted from the spacer's 3' end (1 = last letter).</param>
    /// <param name="mismatchPos">Where it puts the first engineered mismatch, counted the same way.</param>
    /// <param name="mismatchCount">1 or 2: with 2 the next position is engineered too.</param>
    /// <param name="engineeredBases">The "sequence base&gt;guide base" table, e.g. <c>A&gt;A,G&gt;A,C&gt;C,U&gt;C</c>.</param>
    /// <param name="repeat">The screening's direct repeat as <see cref="RunParameters.Repeat"/> holds it: null = default, "" = none.</param>
    public static string ProvenanceLine(int spacerSize, int seedStart, int seedEnd, int snpPos = gRNA.SpacerFinder.DefaultSnpPosition, int mismatchPos = gRNA.SpacerFinder.DefaultMismatchPosition, string? repeat = null,
        int mismatchCount = gRNA.SpacerFinder.DefaultMismatchCount, string engineeredBases = gRNA.SpacerFinder.DefaultEngineeredBases, bool applyRule = true) =>
        gRNA.ReportSchema.provenanceWithRuleAndRepeat(new gRNA.SpacerFinder.SpecialRuleOptions(snpPos, mismatchPos, mismatchCount, engineeredBases, applyRule),
            repeat ?? gRNA.SpacerFinder.scaffold, spacerSize, seedStart, seedEnd);

    public static string Header => string.Join(",", Columns);

    public static string Strand(bool isComplement) => isComplement ? ComplementStrand : NormalStrand;

    public static string Row(string? rsId, string hgvs, string sequenceType, GRNAResult g, bool isComplement)
    {
        var inv = CultureInfo.InvariantCulture;
        var energy = g.RnaFoldResult?.Energy.ToString(inv) ?? "N/A";
        return string.Join(",",
            Csv(rsId ?? ""), Csv(hgvs), sequenceType, g.Rank.ToString(inv), g.Sequence,
            g.Score.ToString(inv), g.GCContent.ToString(inv), g.Offtargets.ToString(inv),
            g.SeedRegion, g.HomopolymerCount.ToString(inv), energy, Strand(isComplement),
            g.WtMismatches < 0 ? "" : g.WtMismatches.ToString(inv), g.Engineered ? "Yes" : "No",
            g.ScaffoldIntact ? "Yes" : "No",
            g.SpacerUnpaired < 0 ? "" : Math.Round(g.SpacerUnpaired, 2).ToString(inv));
    }

    public static void AppendRows(StringBuilder sb, string? rsId, string hgvs, IEnumerable<GRNAResult>? grnas, string sequenceType, bool isComplement)
    {
        if (grnas == null) return;
        foreach (var g in grnas)
            sb.AppendLine(Row(rsId, hgvs, sequenceType, g, isComplement));
    }

    /// <summary>
    /// Quotes a field only when needed (commas, quotes, newlines) and neutralises spreadsheet formulas (a leading
    /// = + - @ gets an apostrophe). One definition, shared with the CLI: <c>gRNA.ReportSchema.csvField</c>.
    /// </summary>
    public static string Csv(string value) => gRNA.ReportSchema.csvField(value);
}
