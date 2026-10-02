using System.Globalization;
using System.Text;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// Single source of truth for the gRNA Builder CSV report: the web export writes it,
/// <see cref="GuideListParser"/> reads it, and the tests assert they agree.
/// </summary>
public static class GrnaCsvSchema
{
    public static readonly string[] Columns =
    {
        "RS ID", "HGVS", "Sequence Type", "Rank", "Sequence", "Score", "GC Content",
        "Alignments", "Seed Region", "Homopolymers", "Fold Energy", "Strand"
    };

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
    public static string ProvenanceLine(int spacerSize, int seedStart, int seedEnd) =>
        $"# gRNA Builder report; spacer={spacerSize}; seed={seedStart}-{seedEnd}; generated={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";

    public static string Header => string.Join(",", Columns);

    public static string Strand(bool isComplement) => isComplement ? ComplementStrand : NormalStrand;

    public static string Row(string? rsId, string hgvs, string sequenceType, GRNAResult g, bool isComplement)
    {
        var inv = CultureInfo.InvariantCulture;
        var energy = g.RnaFoldResult?.Energy.ToString(inv) ?? "N/A";
        return string.Join(",",
            Csv(rsId ?? ""), Csv(hgvs), sequenceType, g.Rank.ToString(inv), g.Sequence,
            g.Score.ToString(inv), g.GCContent.ToString(inv), g.Allignments.ToString(inv),
            g.SeedRegion, g.HomopolymerCount.ToString(inv), energy, Strand(isComplement));
    }

    public static void AppendRows(StringBuilder sb, string? rsId, string hgvs, IEnumerable<GRNAResult>? grnas, string sequenceType, bool isComplement)
    {
        if (grnas == null) return;
        foreach (var g in grnas)
            sb.AppendLine(Row(rsId, hgvs, sequenceType, g, isComplement));
    }

    /// <summary>Quotes a field only when needed (commas, quotes, newlines).</summary>
    public static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
