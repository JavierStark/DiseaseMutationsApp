using System;
using System.Collections.Generic;
using DiseaseMutationsApp.Pages;

namespace DiseaseMutationsApp.Services
{
    /// <summary>
    /// Scoped service that maintains state across page navigations.
    /// This service persists data for both the Index (gRNA Builder) and OmimToRs pages.
    /// </summary>
    public class AppStateService
    {
        // ===== Index Page State =====
        public string? IndexHgvsInput { get; set; }
        public int IndexGRnaSize { get; set; } = 28;
        public int IndexSeedStart { get; set; } = 10;
        public int IndexSeedEnd { get; set; } = 17;
        public List<InputTabData> IndexInputTabs { get; set; } = new();
        public int IndexActiveTabIndex { get; set; }
        public Dictionary<int, int> IndexActiveChildTabIndices { get; set; } = new();

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
            IndexInputTabs.Clear();
            IndexActiveTabIndex = 0;
            IndexActiveChildTabIndices.Clear();

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
            IndexInputTabs.Clear();
            IndexActiveTabIndex = 0;
            IndexActiveChildTabIndices.Clear();
            NotifyStateChanged();
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
    }
}

