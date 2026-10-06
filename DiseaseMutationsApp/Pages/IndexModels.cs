using System.Collections.Generic;
using DiseaseMutationsApp.Services;

namespace DiseaseMutationsApp.Pages
{
    public enum InputType
    {
        HGVS,
        RS
    }

    public enum GrnaSortColumn { Sequence, GCScore, GCContent, HomopolymerCount, Alignments, Energy, Score }

    /// <summary>Lifecycle of one analysed variant (leaf).</summary>
    public enum LeafStatus { Queued, Running, Ready, Failed, Cancelled }

    public class InputTabData
    {
        public Guid Id { get; } = Guid.NewGuid();
        public InputType Type { get; init; }
        public string DisplayLabel { get; init; } = string.Empty;
        public string? RsId { get; init; }
        public string? ErrorMessage { get; set; }
        public bool IsLoading { get; set; }

        // For direct HGVS input
        public HgvsData? DirectHgvs { get; set; }

        // For RS input with child HGVS. Always replaced wholesale, never mutated while rendered.
        public List<HgvsData>? ChildHgvsList { get; set; }

        public IEnumerable<HgvsData> Leaves =>
            DirectHgvs is not null ? new[] { DirectHgvs } : ChildHgvsList ?? Enumerable.Empty<HgvsData>();
    }

    public class HgvsData
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Hgvs { get; init; } = string.Empty;
        public bool IsComplement { get; init; }

        public LeafStatus Status { get; set; } = LeafStatus.Queued;
        public bool IsLoading => Status is LeafStatus.Queued or LeafStatus.Running;

        public string? Original { get; set; }
        public string? Mutated { get; set; }
        public string? SourceUrl { get; set; }
        public int? ExtraNucleotids { get; set; }
        public int? WindowStart { get; set; }
        public int? WindowEnd { get; set; }

        /// <summary>Accession and 1-based inclusive window, e.g. NC_000017.11:43045690-43045734.</summary>
        public string? Locus => WindowStart is > 0 && WindowEnd is > 0
            ? $"{Hgvs.Split(':', 2)[0].Trim()}:{WindowStart}-{WindowEnd}"
            : null;

        public List<GRNAResult>? GRNAs { get; set; }
        public List<GRNAResult>? OriginalGRNAs { get; set; }
        public string? ErrorMessage { get; set; }

        // View state (survives tab switches because only the active panel is rendered)
        public string? SelectedSpacer { get; set; }
        public bool CopiedToClipboard { get; set; }
        public bool IsLoadingRnaFold { get; set; }
        public RNAFoldResult? RnaFoldResult { get; set; }
        public string? RnaFoldError { get; set; }
        public string? FornaUrl { get; set; }
        public bool ShowStructure { get; set; }
        public GrnaSortColumn SortColumn { get; set; } = GrnaSortColumn.Score;
        public bool SortAscending { get; set; }
        public GrnaSortColumn OriginalSortColumn { get; set; } = GrnaSortColumn.Score;
        public bool OriginalSortAscending { get; set; }
        public GrnaFilter Filter { get; set; } = new();
        public GrnaFilter OriginalFilter { get; set; } = new();

        /// <summary>Clears results so the leaf can be re-run.</summary>
        public void ResetForRun()
        {
            Status = LeafStatus.Queued;
            ErrorMessage = null;
        }
    }

    /// <summary>Client-side filters over a candidate table.</summary>
    public class GrnaFilter
    {
        public double? MinScore { get; set; }
        public int? MaxAlignments { get; set; }
        public double? MinGc { get; set; }
        public double? MaxGc { get; set; }
        public string? Contains { get; set; }
        public int PageSize { get; set; } = 25;
        public int Page { get; set; }

        public bool IsActive =>
            MinScore.HasValue || MaxAlignments.HasValue || MinGc.HasValue || MaxGc.HasValue || !string.IsNullOrWhiteSpace(Contains);

        public bool Matches(GRNAResult g) =>
            (!MinScore.HasValue || g.Score >= MinScore.Value)
            && (!MaxAlignments.HasValue || g.Allignments <= MaxAlignments.Value)
            && (!MinGc.HasValue || g.GCContent >= MinGc.Value)
            && (!MaxGc.HasValue || g.GCContent <= MaxGc.Value)
            && (string.IsNullOrWhiteSpace(Contains) || g.Sequence.Contains(Contains.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
