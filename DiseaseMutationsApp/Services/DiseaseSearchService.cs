using System.Text;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static gRNA.DiseaseVariants;

namespace DiseaseMutationsApp.Services;

/// <summary>Which ClinVar germline classifications a search keeps.</summary>
public enum DiseaseSignificance
{
    PathogenicOrLikely,
    PathogenicOnly,
    Any
}

/// <summary>A disease Monarch knows, as the Disease variants page shows it.</summary>
public sealed record DiseaseMatchDto(string Id, string Name, IReadOnlyList<string> OmimNumbers, IReadOnlyList<string> PhenotypicSeries)
{
    /// <summary>A phenotypic series (or a group disease) has no OMIM number of its own: its subtypes must be consulted.</summary>
    public bool NeedsSubtypes => OmimNumbers.Count == 0;
}

/// <summary>One ClinVar variant of a disease.</summary>
public sealed record DiseaseVariantDto(
    string ClinVarId, string Name, string? Gene, string? RsId, string? Hgvs,
    string Significance, string ReviewStatus, int Stars, IReadOnlyList<string> Conditions)
{
    /// <summary>What Diana is given for this variant: the rsID, or the genomic HGVS when ClinVar has no rsID. Null when neither exists.</summary>
    public string? DianaInput => RsId ?? Hgvs;
}

public sealed record DiseaseProgressDto(string Stage, int Done, int Total);

public sealed record DiseaseSearchResultDto(
    IReadOnlyList<string> OmimNumbers, int SubtypesChecked, int SubtypesTotal,
    int TotalMatching, bool Truncated, IReadOnlyList<DiseaseVariantDto> Variants);

/// <summary>The slice of the disease search the page depends on; lets tests substitute a fake.</summary>
public interface IDiseaseVariantSource
{
    /// <summary>Diseases matching a name, OMIM number or MONDO id. Bad input raises GrnaInputException; Monarch failures GrnaUpstreamException.</summary>
    Task<IReadOnlyList<DiseaseMatchDto>> FindDiseasesAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Resolves the disease's OMIM numbers (through its subtypes when asked or needed) and reads their ClinVar variants.</summary>
    Task<DiseaseSearchResultDto> FindVariantsAsync(DiseaseMatchDto disease, bool includeSubtypes, DiseaseSignificance minimum,
        IProgress<DiseaseProgressDto>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Monarch (disease names, subtypes) plus NCBI ClinVar (variants), through the gRNA library.</summary>
public sealed class DiseaseSearchService : IDiseaseVariantSource
{
    private readonly ILogger<DiseaseSearchService> _logger;
    private readonly DiseaseVariantClient _client;

    public DiseaseSearchService(ILogger<DiseaseSearchService> logger) : this(logger, DiseaseVariantClient.Shared) { }

    public DiseaseSearchService(ILogger<DiseaseSearchService> logger, DiseaseVariantClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task<IReadOnlyList<DiseaseMatchDto>> FindDiseasesAsync(string text, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Disease search: {Text}", text);   // what a user typed is logged at Debug only
        var matches = await _client.FindDiseasesAsync(text, cancellationToken);
        return matches.Select(ToDto).ToList();
    }

    public async Task<DiseaseSearchResultDto> FindVariantsAsync(DiseaseMatchDto disease, bool includeSubtypes, DiseaseSignificance minimum,
        IProgress<DiseaseProgressDto>? progress = null, CancellationToken cancellationToken = default)
    {
        var match = new DiseaseMatch(disease.Id, disease.Name, ListModule.OfSeq(disease.OmimNumbers),
            ListModule.OfSeq(disease.PhenotypicSeries), ListModule.Empty<DiseaseRef>());
        var resolved = await _client.ResolveOmimNumbersAsync(match, includeSubtypes, cancellationToken);
        if (resolved.OmimNumbers.IsEmpty)
            return new DiseaseSearchResultDto(Array.Empty<string>(), resolved.SubtypesChecked, resolved.SubtypesTotal, 0, false, Array.Empty<DiseaseVariantDto>());

        var libraryProgress = new RelayProgress(p => progress?.Report(new DiseaseProgressDto(p.Stage, p.Done, p.Total)));
        var result = await _client.GetVariantsAsync(resolved.OmimNumbers, ToLibrary(minimum), libraryProgress, cancellationToken);
        _logger.LogDebug("Disease {Id}: {Total} ClinVar records matched, {Listed} listed", disease.Id, result.TotalMatching, result.Variants.Length);
        return new DiseaseSearchResultDto(resolved.OmimNumbers.ToList(), resolved.SubtypesChecked, resolved.SubtypesTotal,
            result.TotalMatching, result.Truncated, result.Variants.Select(ToDto).ToList());
    }

    /// <summary>Synchronous progress relay: Progress&lt;T&gt; would post to the sync context and reorder reports.</summary>
    private sealed class RelayProgress : IProgress<VariantProgress>
    {
        private readonly Action<VariantProgress> _report;
        public RelayProgress(Action<VariantProgress> report) => _report = report;
        public void Report(VariantProgress value) => _report(value);
    }

    private static MinSignificance ToLibrary(DiseaseSignificance s) => s switch
    {
        DiseaseSignificance.PathogenicOnly => MinSignificance.PathogenicOnly,
        DiseaseSignificance.Any => MinSignificance.AnyClassification,
        _ => MinSignificance.PathogenicOrLikely
    };

    private static DiseaseMatchDto ToDto(DiseaseMatch m) =>
        new(m.Id, m.Name, m.OmimNumbers.ToList(), m.PhenotypicSeries.ToList());

    private static DiseaseVariantDto ToDto(DiseaseVariant v) =>
        new(v.ClinVarId, v.Name, Opt(v.Gene), Opt(v.RsId), Opt(v.Hgvs), v.Significance, v.ReviewStatus, reviewStars(v.ReviewStatus), v.Traits.ToList());

    private static string? Opt(FSharpOption<string>? o) => o is null ? null : o.Value;
}

/// <summary>Text exports of a selection or of the whole result, written by the library's own formatters.</summary>
public static class DiseaseExport
{
    private static DiseaseVariant ToLibrary(DiseaseVariantDto v) =>
        new(v.ClinVarId, v.Name,
            v.Gene is null ? FSharpOption<string>.None : FSharpOption<string>.Some(v.Gene),
            v.RsId is null ? FSharpOption<string>.None : FSharpOption<string>.Some(v.RsId),
            v.Hgvs is null ? FSharpOption<string>.None : FSharpOption<string>.Some(v.Hgvs),
            v.Significance, v.ReviewStatus, ListModule.OfSeq(v.Conditions));

    /// <summary>One distinct rsID per line (variants without an rsID are not listed).</summary>
    public static string RsList(IEnumerable<DiseaseVariantDto> variants) =>
        formatRsList(ListModule.OfSeq(variants.Select(ToLibrary)));

    public static string Csv(IEnumerable<DiseaseVariantDto> variants) =>
        formatCsv(ListModule.OfSeq(variants.Select(ToLibrary)));

    /// <summary>What Diana is given, one per line: rsIDs, or the genomic HGVS where there is no rsID. Distinct, order kept.</summary>
    public static string InputList(IEnumerable<DiseaseVariantDto> variants) =>
        string.Concat(DianaInputs(variants).Select(i => i + "\n"));

    public static List<string> DianaInputs(IEnumerable<DiseaseVariantDto> variants) =>
        variants.Select(v => v.DianaInput).Where(i => i is not null).Select(i => i!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A file name stem from a disease name: letters, digits, dot, dash and underscore only.</summary>
    public static string FileStem(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        var stem = sb.ToString().Trim('_');
        return stem.Length == 0 ? "disease" : stem.Length > 40 ? stem[..40] : stem;
    }
}
