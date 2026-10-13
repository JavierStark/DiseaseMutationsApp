using System;
using System.Collections.Generic;
using System.Linq;
using DiseaseMutationsApp.Pages;

namespace DiseaseMutationsApp.Services
{
    /// <summary>
    /// Scoped service that maintains state across page navigations.
    /// This service persists data for the Diana (Index) and Guide Pooling pages.
    /// </summary>
    public class AppStateService
    {
        // ===== Index Page State =====
        public string? IndexHgvsInput { get; set; }
        public int IndexGRnaSize { get; set; } = 28;
        public int IndexSeedStart { get; set; } = 10;
        public int IndexSeedEnd { get; set; } = 17;
        /// <summary>Replaced wholesale (never mutated in place) so a render never sees a half-built list.</summary>
        public List<InputTabData> IndexInputTabs { get; set; } = new();
        public int IndexActiveTabIndex { get; set; }
        /// <summary>Active leaf per RS tab, keyed by tab id then leaf id.</summary>
        public Dictionary<Guid, Guid> IndexActiveLeafIds { get; set; } = new();

        /// <summary>The query string the Builder page last acted on, so a URL is claimed only once.</summary>
        public string? IndexClaimedQuery { get; set; }

        /// <summary>Guides picked across variants, ready to send to the Pooling page.</summary>
        public List<ShortlistItem> Shortlist { get; set; } = new();

        /// <summary>Set when variants were sent from the Disease variants page; the Diana page shows it once, then clears it.</summary>
        public IncomingNotice? IndexIncomingNotice { get; set; }

        // ===== Disease variants page state =====
        public DiseaseSearchState DiseaseSearch { get; set; } = new();

        // ===== Guide Pooling Page State =====
        public PoolingInputMode PoolingInputMode { get; set; } = PoolingInputMode.GuideCount;
        public int PoolingGuideCount { get; set; } = 100;
        public string? PoolingGuideListText { get; set; }
        public List<GuideEntry> PoolingParsedGuides { get; set; } = new();
        public List<string> PoolingParseWarnings { get; set; } = new();
        public GuideListSource PoolingGuideListSource { get; set; } = GuideListSource.PlainList;
        public int PoolingWellCapacity { get; set; } = 5;
        public PlateKind PoolingPlate { get; set; } = PlateKind.Plate96;

        /// <summary>Null means "use whichever model the comparison says is cheapest".</summary>
        public PoolingModelKind? PoolingSelectedModel { get; set; }

        public List<PoolingModelEstimate>? PoolingEstimates { get; set; }
        public PoolingPlanDto? PoolingPlan { get; set; }
        public int PoolingActivePlate { get; set; } = 1;
        public int? PoolingSelectedPoolId { get; set; }
        public string? PoolingErrorMessage { get; set; }

        // Events to notify components when state changes
        public event Action? OnStateChanged;

        public void NotifyStateChanged() => OnStateChanged?.Invoke();

        /// <summary>
        /// Clear all state data
        /// </summary>
        public void ClearAll()
        {
            // Clear Index state
            IndexHgvsInput = null;
            IndexGRnaSize = 28;
            IndexSeedStart = 10;
            IndexSeedEnd = 17;
            IndexInputTabs = new();
            IndexActiveTabIndex = 0;
            IndexActiveLeafIds = new();
            IndexClaimedQuery = null;
            IndexIncomingNotice = null;
            DiseaseSearch = new();

            // Clear Guide Pooling state
            ResetPoolingState();

            NotifyStateChanged();
        }

        /// <summary>
        /// Clear only Index page state
        /// </summary>
        public void ClearIndexState()
        {
            IndexHgvsInput = null;
            IndexGRnaSize = 28;
            IndexSeedStart = 10;
            IndexSeedEnd = 17;
            IndexInputTabs = new();
            IndexActiveTabIndex = 0;
            IndexActiveLeafIds = new();
            IndexClaimedQuery = null;
            IndexIncomingNotice = null;
            NotifyStateChanged();
        }

        /// <summary>
        /// Clear only Guide Pooling page state
        /// </summary>
        public void ClearPoolingState()
        {
            ResetPoolingState();
            NotifyStateChanged();
        }

        private void ResetPoolingState()
        {
            PoolingInputMode = PoolingInputMode.GuideCount;
            PoolingGuideCount = 100;
            PoolingGuideListText = null;
            PoolingParsedGuides = new();
            PoolingParseWarnings = new();
            PoolingGuideListSource = GuideListSource.PlainList;
            PoolingWellCapacity = 5;
            PoolingPlate = PlateKind.Plate96;
            PoolingSelectedModel = null;
            PoolingEstimates = null;
            PoolingPlan = null;
            PoolingActivePlate = 1;
            PoolingSelectedPoolId = null;
            PoolingErrorMessage = null;
        }

        // ===== Hand-off to the Diana page =====

        /// <summary>
        /// Puts rsIDs (or genomic HGVS) into the Diana page's input box, replacing its content or adding to it. Duplicates
        /// (case-insensitive) are dropped. It only fills the form: a run starts when the user presses Run analysis.
        /// </summary>
        public IncomingNotice SendToBuilder(IEnumerable<string> inputs, bool append, string source = "Disease variants")
        {
            var existing = append ? AnalysisRunner.ParseInputs(IndexHgvsInput) : new List<string>();
            var present = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            var incoming = AnalysisRunner.ParseInputs(string.Join(",", inputs));
            var added = incoming.Where(present.Add).ToList();

            IndexHgvsInput = string.Join(", ", existing.Concat(added));
            IndexClaimedQuery = null;
            var notice = new IncomingNotice(source, added.Count, incoming.Count - added.Count, existing.Count + added.Count, append);
            IndexIncomingNotice = notice;
            NotifyStateChanged();
            return notice;
        }

        // ===== Shortlist (cross-variant basket) =====

        public bool IsShortlisted(string hgvs, bool isComplement) =>
            Shortlist.Any(i => i.Hgvs == hgvs && i.IsComplement == isComplement);

        public void AddToShortlist(ShortlistItem item)
        {
            Shortlist = Shortlist
                .Where(i => !(i.Hgvs == item.Hgvs && i.IsComplement == item.IsComplement))
                .Append(item)
                .ToList();
            NotifyStateChanged();
        }

        public void RemoveFromShortlist(string hgvs, bool isComplement)
        {
            Shortlist = Shortlist.Where(i => !(i.Hgvs == hgvs && i.IsComplement == isComplement)).ToList();
            NotifyStateChanged();
        }

        public void ClearShortlist()
        {
            Shortlist = new();
            NotifyStateChanged();
        }

        /// <summary>Hands the shortlist to the Pooling page as typed guide entries (no CSV round trip).</summary>
        public void SendShortlistToPooling()
        {
            var guides = Shortlist
                .Select(i => new GuideEntry { Label = i.Hgvs, Sequence = i.Spacer, RsId = i.RsId })
                .ToList();
            ResetPoolingState();
            PoolingInputMode = PoolingInputMode.GuideList;
            PoolingParsedGuides = guides;
            PoolingGuideListSource = GuideListSource.PlainList;
            PoolingGuideListText = string.Join(Environment.NewLine, guides.Select(g => g.Label));
            NotifyStateChanged();
        }
    }

    /// <summary>What a hand-off to the Diana page changed: how many inputs were added, how many were already there, and how many are in the box now.</summary>
    public record IncomingNotice(string Source, int Added, int AlreadyPresent, int Total, bool Appended)
    {
        public string Message =>
            $"{Source}: {Added} input{(Added == 1 ? "" : "s")} {(Appended ? "added" : "put in the box")}"
            + (AlreadyPresent > 0 ? $" ({AlreadyPresent} already there)" : "")
            + (Appended ? $", {Total} in total." : ".");
    }

    /// <summary>The Disease variants page's inputs, matches, result and selection, kept while the user visits another page.</summary>
    public sealed class DiseaseSearchState
    {
        public string Query { get; set; } = "";
        public string? ClaimedQuery { get; set; }
        public IReadOnlyList<DiseaseMatchDto>? Matches { get; set; }
        public string? SelectedMatchId { get; set; }
        public bool IncludeSubtypes { get; set; }
        public DiseaseSignificance Significance { get; set; } = DiseaseSignificance.PathogenicOrLikely;
        public DiseaseMatchDto? ResultDisease { get; set; }
        public DiseaseSearchResultDto? Result { get; set; }
        /// <summary>ClinVar ids of the selected rows.</summary>
        public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);
        public string FilterText { get; set; } = "";
        public bool OnlyWithRsId { get; set; }
        public int PageSize { get; set; } = 25;
        public int Page { get; set; }
        public DiseaseSortColumn SortColumn { get; set; } = DiseaseSortColumn.None;
        public bool SortAscending { get; set; } = true;
    }

    public enum DiseaseSortColumn
    {
        None,
        Variant,
        Gene,
        Name,
        Classification,
        Stars
    }

    /// <summary>One chosen spacer for one variant strand.</summary>
    public record ShortlistItem(string Hgvs, bool IsComplement, string Spacer, string? RsId, double Score, int Alignments);
}

