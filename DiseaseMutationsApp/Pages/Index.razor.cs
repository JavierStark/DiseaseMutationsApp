using System.Text;
using DiseaseMutationsApp.Components;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiseaseMutationsApp.Pages
{
    public partial class Index : ComponentBase, IDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private AppStateService StateService { get; set; } = default!;
        [Inject] private AnalysisRunner Runner { get; set; } = default!;
        [Inject] private SessionStorageService Session { get; set; } = default!;

        // The single URL entry point: query parameters are claimed once, from OnParametersSetAsync only.
        [SupplyParameterFromQuery(Name = "rs")] private string? RsQuery { get; set; }
        [SupplyParameterFromQuery(Name = "hgvs")] private string? HgvsQuery { get; set; }
        [SupplyParameterFromQuery(Name = "spacer")] private int? SpacerQuery { get; set; }
        [SupplyParameterFromQuery(Name = "seed")] private string? SeedQuery { get; set; }

        private readonly Func<Action, Task> _marshal;
        private bool _disposed;
        private bool _restoredBanner;
        private bool _linkCopied;

        public Index()
        {
            _marshal = action => InvokeAsync(action);
        }

        // ===== Input state proxies onto AppStateService =====

        private string? _hgvs
        {
            get => StateService.IndexHgvsInput;
            set => StateService.IndexHgvsInput = value;
        }

        private int _gRnaSize
        {
            get => StateService.IndexGRnaSize;
            set => StateService.IndexGRnaSize = value;
        }

        private int _seedStart
        {
            get => StateService.IndexSeedStart;
            set => StateService.IndexSeedStart = value;
        }

        private int _seedEnd
        {
            get => StateService.IndexSeedEnd;
            set => StateService.IndexSeedEnd = value;
        }

        private bool CanFetchData =>
            !Runner.IsBusy && !string.IsNullOrWhiteSpace(_hgvs) && _gRnaSize > 0 && SeedRangeValidationMessage == null;

        private string? SeedRangeValidationMessage
        {
            get
            {
                if (_gRnaSize <= 0) return "Spacer size must be greater than 0.";
                if (_seedStart < 0) return "Seed start must be between 0 and spacer size - 1.";
                if (_seedEnd < 0) return "Seed end must be between 0 and spacer size - 1.";
                if (_seedStart >= _gRnaSize || _seedEnd >= _gRnaSize) return $"Seed range must stay within 0 to {_gRnaSize - 1}.";
                if (_seedStart > _seedEnd) return "Seed start must be less than or equal to seed end.";
                return null;
            }
        }

        private IReadOnlyList<InputTabData> _inputTabs => StateService.IndexInputTabs;

        private static string Key(InputTabData tab) => tab.Id.ToString("N");
        private static string Key(HgvsData leaf) => leaf.Id.ToString("N");

        private InputTabData? ActiveTab =>
            _inputTabs.Count == 0 ? null : _inputTabs[Math.Clamp(StateService.IndexActiveTabIndex, 0, _inputTabs.Count - 1)];

        private HgvsData? ActiveLeaf(InputTabData tab)
        {
            var leaves = tab.ChildHgvsList;
            if (leaves is null || leaves.Count == 0) return null;
            // Re-keyed by id, with a fallback to the first leaf: a missing key must never mean a blank panel.
            return StateService.IndexActiveLeafIds.TryGetValue(tab.Id, out var id)
                ? leaves.FirstOrDefault(l => l.Id == id) ?? leaves[0]
                : leaves[0];
        }

        private static string InputPrefix => "input";
        private static string LeafPrefix(InputTabData tab) => $"leaf-{Key(tab)}";
        private static string LeafLabel(InputTabData tab) => $"Variants of {tab.DisplayLabel}";
        private static string InputPanelId(InputTabData tab) => TabStrip<InputTabData>.PanelId(InputPrefix, Key(tab));
        private static string InputTabId(InputTabData tab) => TabStrip<InputTabData>.TabId(InputPrefix, Key(tab));
        private static string LeafPanelId(InputTabData tab, HgvsData leaf) => TabStrip<HgvsData>.PanelId(LeafPrefix(tab), Key(leaf));
        private static string LeafTabId(InputTabData tab, HgvsData leaf) => TabStrip<HgvsData>.TabId(LeafPrefix(tab), Key(leaf));

        private void SelectTab(string key)
        {
            var index = _inputTabs.ToList().FindIndex(t => Key(t) == key);
            if (index >= 0) StateService.IndexActiveTabIndex = index;
        }

        private void SelectLeaf(InputTabData tab, string key)
        {
            var leaf = tab.ChildHgvsList?.FirstOrDefault(l => Key(l) == key);
            if (leaf != null) StateService.IndexActiveLeafIds[tab.Id] = leaf.Id;
        }

        private static TabState StateOf(HgvsData leaf) => leaf.Status switch
        {
            LeafStatus.Queued => TabState.Queued,
            LeafStatus.Running => TabState.Loading,
            LeafStatus.Failed => TabState.Failed,
            LeafStatus.Cancelled => TabState.Cancelled,
            _ => TabState.Ready
        };

        private static TabState StateOf(InputTabData tab)
        {
            if (tab.ErrorMessage != null) return TabState.Failed;
            if (tab.IsLoading) return TabState.Loading;
            if (tab.DirectHgvs is { } leaf) return StateOf(leaf);
            var leaves = tab.ChildHgvsList;
            if (leaves is null) return TabState.Loading;
            if (leaves.Any(l => l.IsLoading)) return TabState.Loading;
            if (leaves.Count > 0 && leaves.All(l => l.Status == LeafStatus.Failed)) return TabState.Failed;
            return TabState.Ready;
        }

        // ===== Lifecycle =====

        protected override void OnInitialized()
        {
            Runner.Attach(_marshal);
            Runner.Changed += OnRunnerChanged;
            StateService.OnStateChanged += OnRunnerChanged;
        }

        protected override Task OnParametersSetAsync()
        {
            ClaimUrlOnce();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Acts on the query string exactly once per distinct query. Prerendering is off, so this runs only in
        /// the circuit; the claim lives in circuit-scoped state, so navigation back to "/" does not re-run it.
        /// </summary>
        private void ClaimUrlOnce()
        {
            var input = !string.IsNullOrWhiteSpace(HgvsQuery) ? HgvsQuery : RsQuery;
            if (string.IsNullOrWhiteSpace(input)) return;

            var claim = $"{input}|{SpacerQuery}|{SeedQuery}";
            if (StateService.IndexClaimedQuery == claim) return;
            StateService.IndexClaimedQuery = claim;

            _hgvs = input.Trim();
            if (SpacerQuery is > 0) _gRnaSize = SpacerQuery.Value;
            if (!string.IsNullOrWhiteSpace(SeedQuery))
            {
                var parts = SeedQuery.Split('-', 2);
                if (parts.Length == 2 && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var e))
                {
                    _seedStart = s;
                    _seedEnd = e;
                }
            }

            if (CanFetchData) StartRun();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            // Live circuit state always beats storage; restoring never starts a run.
            if (Runner.Phase != RunPhase.Idle || _inputTabs.Count > 0 || !string.IsNullOrWhiteSpace(_hgvs)) return;

            var snapshot = await Session.TryLoadAsync();
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Input)) return;
            if (_disposed) return;

            _hgvs = snapshot.Input;
            if (snapshot.SpacerSize > 0) _gRnaSize = snapshot.SpacerSize;
            _seedStart = snapshot.SeedStart;
            _seedEnd = snapshot.SeedEnd;
            _restoredBanner = true;
            StateHasChanged();
        }

        private void OnRunnerChanged()
        {
            if (_disposed) return;
            _ = InvokeAsync(() =>
            {
                if (_disposed) return;
                try { StateHasChanged(); }
                catch (ObjectDisposedException) { }
            });
        }

        // ===== Actions =====

        private void StartRun()
        {
            if (!CanFetchData) return;
            _restoredBanner = false;
            SaveSession();
            Runner.TryStart(_hgvs, new RunParameters(_gRnaSize, _seedStart, _seedEnd));
        }

        private void SaveSession()
        {
            Session.SaveDebounced(new SessionSnapshot(
                SessionSnapshot.CurrentVersion, _hgvs, _gRnaSize, _seedStart, _seedEnd, ActiveTab?.DisplayLabel, DateTimeOffset.UtcNow));
        }

        private void OnInputChanged() => SaveSession();

        private void CancelRun() => Runner.Cancel();

        private async Task CopyPermalink()
        {
            if (string.IsNullOrWhiteSpace(_hgvs)) return;
            var url = Nav.GetUriWithQueryParameters(Nav.ToAbsoluteUri("").GetLeftPart(UriPartial.Path), new Dictionary<string, object?>
            {
                ["rs"] = null,
                ["hgvs"] = _hgvs.Trim(),
                ["spacer"] = _gRnaSize,
                ["seed"] = $"{_seedStart}-{_seedEnd}"
            });
            try
            {
                _linkCopied = await JSRuntime.InvokeAsync<bool>("grnaCopy", url);
            }
            catch (JSDisconnectedException) { }
        }

        // ===== Export =====

        private async Task DownloadReport(InputTabData tabData)
        {
            if (tabData.ChildHgvsList == null || !tabData.ChildHgvsList.Any()) return;

            var sb = new StringBuilder();
            AppendProvenance(sb);
            sb.AppendLine(GrnaCsvSchema.Header);
            foreach (var hgvs in tabData.ChildHgvsList)
            {
                GrnaCsvSchema.AppendRows(sb, tabData.RsId, hgvs.Hgvs, hgvs.GRNAs, GrnaCsvSchema.MutatedType, hgvs.IsComplement);
                GrnaCsvSchema.AppendRows(sb, tabData.RsId, hgvs.Hgvs, hgvs.OriginalGRNAs, GrnaCsvSchema.OriginalType, hgvs.IsComplement);
            }

            var fileName = $"Report_RS{tabData.RsId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, "text/csv;charset=utf-8", sb.ToString());
        }

        private async Task DownloadAllRsReports()
        {
            var allRsTabs = _inputTabs.Where(t => t.Type == InputType.RS && t.ChildHgvsList != null).ToList();
            if (!allRsTabs.Any()) return;

            var sb = new StringBuilder();
            AppendProvenance(sb);
            sb.AppendLine(GrnaCsvSchema.Header);
            foreach (var tabData in allRsTabs)
            {
                foreach (var hgvs in tabData.ChildHgvsList!)
                {
                    GrnaCsvSchema.AppendRows(sb, tabData.RsId, hgvs.Hgvs, hgvs.GRNAs, GrnaCsvSchema.MutatedType, hgvs.IsComplement);
                    GrnaCsvSchema.AppendRows(sb, tabData.RsId, hgvs.Hgvs, hgvs.OriginalGRNAs, GrnaCsvSchema.OriginalType, hgvs.IsComplement);
                }
            }

            var fileName = $"Report_AllRS_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, "text/csv;charset=utf-8", sb.ToString());
        }

        /// <summary>Parameters go in the file so an exported report can be reproduced (leading # lines are ignored by the parser).</summary>
        private void AppendProvenance(StringBuilder sb)
        {
            var p = Runner.Parameters ?? new RunParameters(_gRnaSize, _seedStart, _seedEnd);
            sb.AppendLine(GrnaCsvSchema.ProvenanceLine(p.SpacerSize, p.SeedStart, p.SeedEnd));
        }

        private void SendShortlistToPooling()
        {
            StateService.SendShortlistToPooling();
            Nav.NavigateTo("pooling");
        }

        public void Dispose()
        {
            _disposed = true;
            Runner.Detach(_marshal);
            Runner.Changed -= OnRunnerChanged;
            StateService.OnStateChanged -= OnRunnerChanged;
        }
    }
}
