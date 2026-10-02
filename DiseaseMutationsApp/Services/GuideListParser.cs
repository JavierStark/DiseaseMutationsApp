using System.Globalization;

namespace DiseaseMutationsApp.Services;

public enum GuideListSource
{
    /// <summary>One guide per line, or comma separated.</summary>
    PlainList,

    /// <summary>The CSV report exported by the gRNA Builder tab.</summary>
    BuilderCsv
}

public record GuideEntry
{
    /// <summary>What the researcher sees on the plate map: an HGVS notation or a free-text name.</summary>
    public required string Label { get; init; }

    /// <summary>The spacer sequence, when the source carried one.</summary>
    public string? Sequence { get; init; }

    public string? RsId { get; init; }
}

public record ParsedGuideList
{
    public required List<GuideEntry> Guides { get; init; }
    public GuideListSource Source { get; init; }
    public List<string> Warnings { get; init; } = new();
    public int Count => Guides.Count;
}

/// <summary>
/// Turns pasted text into the ordered guide list that drives pooling.
/// Accepts either a plain list or the gRNA Builder's own CSV export, so a researcher can
/// run the Builder, download the report, and paste it straight in.
/// </summary>
public static class GuideListParser
{
    private static readonly string[] BuilderCsvHeaderFields =
        GrnaCsvSchema.Columns.Take(GrnaCsvSchema.RequiredLeadingColumns).ToArray();

    private const string MutatedSequenceType = GrnaCsvSchema.MutatedType;
    private const int ColRsId = GrnaCsvSchema.ColRsId;
    private const int ColHgvs = GrnaCsvSchema.ColHgvs;
    private const int ColSequenceType = GrnaCsvSchema.ColSequenceType;
    private const int ColRank = GrnaCsvSchema.ColRank;
    private const int ColSequence = GrnaCsvSchema.ColSequence;
    private const int ColStrand = GrnaCsvSchema.ColStrand;
    private const int MinCsvColumns = GrnaCsvSchema.MinDataColumns;

    public static ParsedGuideList Parse(string? raw)
    {
        var lines = SplitLines(raw);

        if (lines.Count > 0 && IsBuilderCsvHeader(lines[0]))
        {
            return ParseBuilderCsv(lines);
        }

        return ParsePlainList(lines);
    }

    public static bool LooksLikeBuilderCsv(string? raw)
    {
        var lines = SplitLines(raw);
        return lines.Count > 0 && IsBuilderCsvHeader(lines[0]);
    }

    private static List<string> SplitLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>A line is tab-delimited when it has more tabs than commas.</summary>
    private static char DetectDelimiter(string line) =>
        line.Count(c => c == '\t') > line.Count(c => c == ',') ? '\t' : ',';

    private static bool IsBuilderCsvHeader(string line)
    {
        var fields = line.Split(DetectDelimiter(line));
        return fields.Length >= BuilderCsvHeaderFields.Length
            && fields.Take(BuilderCsvHeaderFields.Length)
                .Select(f => f.Trim())
                .SequenceEqual(BuilderCsvHeaderFields, StringComparer.OrdinalIgnoreCase);
    }

    private static ParsedGuideList ParsePlainList(List<string> lines)
    {
        var warnings = new List<string>();
        var guides = new List<GuideEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;

        foreach (var token in lines
                     .SelectMany(line => line.Split(',', StringSplitOptions.RemoveEmptyEntries))
                     .Select(t => t.Trim())
                     .Where(t => t.Length > 0))
        {
            if (!seen.Add(token))
            {
                duplicates++;
                continue;
            }

            guides.Add(new GuideEntry { Label = token });
        }

        if (duplicates > 0)
        {
            warnings.Add($"Ignored {duplicates} duplicate entr{(duplicates == 1 ? "y" : "ies")}.");
        }

        return new ParsedGuideList
        {
            Guides = guides,
            Source = GuideListSource.PlainList,
            Warnings = warnings
        };
    }

    /// <summary>
    /// The Builder emits one row per candidate spacer, so a single variant appears many times.
    /// Screening needs one guide per variant, so keep only the mutated-sequence rows and take
    /// the best-ranked spacer for each distinct HGVS, preserving first-seen order.
    /// </summary>
    private static ParsedGuideList ParseBuilderCsv(List<string> lines)
    {
        var warnings = new List<string>();
        var bestByHgvs = new Dictionary<string, (int Rank, GuideEntry Entry, int Order)>(StringComparer.OrdinalIgnoreCase);
        var malformed = 0;
        var originalRows = 0;
        var firstStrand = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var otherStrandRows = 0;
        var order = 0;
        var delimiter = DetectDelimiter(lines[0]);

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(delimiter);
            if (fields.Length < MinCsvColumns)
            {
                malformed++;
                continue;
            }

            var hgvs = fields[ColHgvs].Trim();
            if (hgvs.Length == 0)
            {
                malformed++;
                continue;
            }

            if (!string.Equals(fields[ColSequenceType].Trim(), MutatedSequenceType, StringComparison.OrdinalIgnoreCase))
            {
                originalRows++;
                continue;
            }

            // Older exports carry no Strand column; when present, keep only the first strand seen
            // per HGVS so normal and complement rows are never merged.
            var strand = fields.Length > ColStrand ? fields[ColStrand].Trim() : "";
            if (!firstStrand.TryGetValue(hgvs, out var seenStrand))
            {
                firstStrand[hgvs] = strand;
            }
            else if (!string.Equals(seenStrand, strand, StringComparison.OrdinalIgnoreCase))
            {
                otherStrandRows++;
                continue;
            }

            // An unparseable rank sorts last rather than discarding an otherwise usable row.
            var rank = int.TryParse(fields[ColRank].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : int.MaxValue;

            var rsId = fields[ColRsId].Trim();
            var entry = new GuideEntry
            {
                Label = hgvs,
                Sequence = fields[ColSequence].Trim() is { Length: > 0 } seq ? seq : null,
                RsId = rsId.Length > 0 ? rsId : null
            };

            if (bestByHgvs.TryGetValue(hgvs, out var existing))
            {
                if (rank < existing.Rank)
                {
                    bestByHgvs[hgvs] = (rank, entry, existing.Order);
                }
            }
            else
            {
                bestByHgvs[hgvs] = (rank, entry, order++);
            }
        }

        if (originalRows > 0)
        {
            warnings.Add($"Skipped {originalRows} original-sequence row(s); only mutated-sequence guides are pooled.");
        }

        if (otherStrandRows > 0)
        {
            warnings.Add($"Skipped {otherStrandRows} row(s) from a different strand of an already-listed variant; only one strand per variant is pooled.");
        }

        if (malformed > 0)
        {
            warnings.Add($"Skipped {malformed} row(s) that did not have the expected columns.");
        }

        var guides = bestByHgvs.Values
            .OrderBy(v => v.Order)
            .Select(v => v.Entry)
            .ToList();

        if (guides.Count > 0)
        {
            warnings.Add($"Kept the best-ranked mutated spacer for each of the {guides.Count} variant(s) in the report.");
        }

        return new ParsedGuideList
        {
            Guides = guides,
            Source = GuideListSource.BuilderCsv,
            Warnings = warnings
        };
    }
}
