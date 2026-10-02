using System.Text.Json;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// A downloadable, re-openable design session: inputs, parameters and shortlist (never results, which are
/// regenerated or kept as CSV). The `grna design --input` command reads the same file, so a run started in
/// the browser can be reproduced headless and vice versa.
/// </summary>
public record SessionDocument(int Version, string Input, int Spacer, string Seed, List<ShortlistItem> Shortlist)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize(SessionDocument doc) => JsonSerializer.Serialize(doc, Options);

    /// <summary>Returns null for anything that is not a session document of a supported version.</summary>
    public static SessionDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{')) return null;
        try
        {
            var doc = JsonSerializer.Deserialize<SessionDocument>(json, Options);
            return doc is { Version: CurrentVersion } && doc.Input is not null ? doc with { Shortlist = doc.Shortlist ?? new() } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns uploaded text into the input box content: a session document, a Builder CSV report (distinct HGVS
    /// from the second column) or a plain list of HGVS/rsIDs.
    /// </summary>
    public static string InputFromUpload(string text)
    {
        var session = TryParse(text);
        if (session is not null) return session.Input;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        if (lines.Count > 0 && GuideListParser.LooksLikeBuilderCsv(string.Join('\n', lines)))
        {
            var delimiter = lines[0].Count(c => c == '\t') > lines[0].Count(c => c == ',') ? '\t' : ',';
            return string.Join(", ", lines.Skip(1).Select(l => l.Split(delimiter)).Where(f => f.Length > 1)
                .Select(f => f[GrnaCsvSchema.ColHgvs].Trim()).Where(h => h.Length > 0).Distinct());
        }

        return string.Join(", ", lines.SelectMany(l => l.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
