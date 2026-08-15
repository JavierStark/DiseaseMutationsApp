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
    /// <summary>
    /// Prefix of the header written by Index.razor.cs when exporting a report. Matching on the
    /// leading columns keeps this tolerant of extra columns being appended later.
    /// </summary>
    private const string BuilderCsvHeaderPrefix = "RS ID,HGVS,Sequence Type";

    private const string MutatedSequenceType = "Mutated";

    // Column positions in the Builder CSV.
    private const int ColRsId = 0;
    private const int ColHgvs = 1;
    private const int ColSequenceType = 2;
    private const int ColRank = 3;
    private const int ColSequence = 4;
    private const int MinCsvColumns = 5;

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

    private static bool IsBuilderCsvHeader(string line) =>
        line.StartsWith(BuilderCsvHeaderPrefix, StringComparison.OrdinalIgnoreCase);

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
        var order = 0;

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
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
