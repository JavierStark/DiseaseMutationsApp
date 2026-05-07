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

    public class InputTabData
    {
        public InputType Type { get; init; }
        public string DisplayLabel { get; init; } = string.Empty;
        public string? RsId { get; init; }
        public string? ErrorMessage { get; set; }
        public bool IsLoading { get; set; }

        // For direct HGVS input
        public HgvsData? DirectHgvs { get; set; }

        // For RS input with child HGVS
        public List<HgvsData>? ChildHgvsList { get; set; }
    }

    public class HgvsData
    {
        public string Hgvs { get; init; } = string.Empty;
        public string? Original { get; set; }
        public string? Mutated { get; set; }
        public string? SourceUrl { get; set; }
        public int? ExtraNucleotids { get; set; }
        public List<GRNAResult>? GRNAs { get; set; }
        public string? SelectedSpacer { get; set; }
        public bool CopiedToClipboard { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsLoading { get; set; }
        public bool IsLoadingRnaFold { get; set; }
        public RNAFoldResult? RnaFoldResult { get; set; }
        public string? RnaFoldError { get; set; }
        public string? FornaUrl { get; set; }
        // Sorting state for gRNA table
        public GrnaSortColumn SortColumn { get; set; } = GrnaSortColumn.Score;
        public bool SortAscending { get; set; }
    }
}

