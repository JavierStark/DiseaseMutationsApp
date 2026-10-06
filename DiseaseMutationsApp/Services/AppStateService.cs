using System;
using System.Collections.Generic;
using System.Linq;
using DiseaseMutationsApp.Pages;

namespace DiseaseMutationsApp.Services
{
    /// <summary>
    /// Scoped service that maintains state across page navigations.
    /// This service persists data for both the Index (Diana) and OmimToRs pages.
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

        // ===== OmimToRs Page State (deactivated) =====
        // public int? OmimCode { get; set; }
        // public List<string>? OmimRsList { get; set; }
        // public HashSet<string> OmimSelectedRs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        // public string? OmimErrorMessage { get; set; }

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

            // Clear Guide Pooling state
            ResetPoolingState();

            // Clear OmimToRs state (deactivated)
            // OmimCode = null;
            // OmimRsList = null;
            // OmimSelectedRs.Clear();
            // OmimErrorMessage = null;

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

        // /// <summary>
        // /// Clear only OmimToRs page state (deactivated)
        // /// </summary>
        // public void ClearOmimState()
        // {
        //     OmimCode = null;
        //     OmimRsList = null;
        //     OmimSelectedRs.Clear();
        //     OmimErrorMessage = null;
        //     NotifyStateChanged();
        // }

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

    /// <summary>One chosen spacer for one variant strand.</summary>
    public record ShortlistItem(string Hgvs, bool IsComplement, string Spacer, string? RsId, double Score, int Alignments);
}

