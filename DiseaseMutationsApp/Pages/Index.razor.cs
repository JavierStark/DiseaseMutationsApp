using System.Text;
using DiseaseMutationsApp.Components;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
        [SupplyParameterFromQuery(Name = "snp")] private int? SnpQuery { get; set; }
        [SupplyParameterFromQuery(Name = "mm")] private int? MismatchQuery { get; set; }

        private readonly Func<Action, Task> _marshal;
        private bool _disposed;
        private bool _restoredBanner;
        private bool _linkBanner;
        private bool _linkCopied;
        private IncomingNotice? _incoming;

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

        private int _snpPos
        {
            get => StateService.IndexSnpPos;
            set => StateService.IndexSnpPos = value;
        }

        private int _mismatchPos
        {
            get => StateService.IndexMismatchPos;
            set => StateService.IndexMismatchPos = value;
        }

        private RunParameters CurrentParameters => new(_gRnaSize, _seedStart, _seedEnd, _snpPos, _mismatchPos);

        private bool CanFetchData =>
            !Runner.IsBusy && !string.IsNullOrWhiteSpace(_hgvs) && InputCountMessage == null && SeedRangeValidationMessage == null && RuleValidationMessage == null;

        /// <summary>Both positions lie inside the spacer (1 = its 3' end) and differ; null when valid.</summary>
        private string? RuleValidationMessage
        {
            get
            {
                if (_snpPos < 1 || _snpPos > _gRnaSize) return $"Variant position must be between 1 and {_gRnaSize} (counted from the spacer's 3' end).";
                if (_mismatchPos < 1 || _mismatchPos > _gRnaSize) return $"Mismatch position must be between 1 and {_gRnaSize} (counted from the spacer's 3' end).";
                if (_snpPos == _mismatchPos) return "The engineered mismatch cannot be at the variant's own position.";
                return null;
            }
        }

        private string? InputCountMessage
        {
            get
            {
                var count = AnalysisRunner.ParseInputs(_hgvs).Count;
                return count > AnalysisRunner.MaxInputs
                    ? $"At most {AnalysisRunner.MaxInputs} variants per run; you entered {count}. Split them into several runs."
                    : null;
            }
        }

        private string? SeedRangeValidationMessage
        {
            get
            {
                if (_gRnaSize < Main.MinSpacerSize || _gRnaSize > Main.MaxSpacerSize) return $"Spacer size must be between {Main.MinSpacerSize} and {Main.MaxSpacerSize}.";
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
            // Variants sent from the Disease variants page: shown once, never run on arrival.
            _incoming = StateService.IndexIncomingNotice;
            StateService.IndexIncomingNotice = null;

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

            var claim = $"{input}|{SpacerQuery}|{SeedQuery}|{SnpQuery}|{MismatchQuery}";
            if (StateService.IndexClaimedQuery == claim) return;
            StateService.IndexClaimedQuery = claim;

            _hgvs = input.Trim();
            if (SpacerQuery is > 0) _gRnaSize = SpacerQuery.Value;
            if (SnpQuery is > 0) _snpPos = SnpQuery.Value;
            if (MismatchQuery is > 0) _mismatchPos = MismatchQuery.Value;
            if (!string.IsNullOrWhiteSpace(SeedQuery))
            {
                var parts = SeedQuery.Split('-', 2);
                if (parts.Length == 2 && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var e))
                {
                    _seedStart = s;
                    _seedEnd = e;
                }
            }

            // A link only fills the form: a run starts on an explicit click. Opening a link used to start NCBI and Bowtie
            // work on the server at once.
            _linkBanner = true;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            // The sent inputs replace what storage held, so the new content is what a refresh restores.
            if (_incoming is not null) SaveSession();

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
            _linkBanner = false;
            _incoming = null;
            SaveSession();
            Runner.TryStart(_hgvs, CurrentParameters);
        }

        private void SaveSession()
        {
            Session.SaveDebounced(new SessionSnapshot(
                SessionSnapshot.CurrentVersion, _hgvs, _gRnaSize, _seedStart, _seedEnd, ActiveTab?.DisplayLabel, DateTimeOffset.UtcNow));
        }

        private void OnInputChanged() => SaveSession();

        private bool _showAllAlleles
        {
            get => StateService.IndexShowAllAlleles;
            set => StateService.IndexShowAllAlleles = value;
        }

        private void OnShowAllAllelesChanged(ChangeEventArgs e) => _showAllAlleles = e.Value is true;

        /// <summary>Empties the input box and the copy kept in browser storage, so a refresh does not bring the inputs back.</summary>
        private async Task ClearInput()
        {
            _hgvs = null;
            _restoredBanner = false;
            _linkBanner = false;
            _incoming = null;
            await Session.ClearAsync();
        }

        private void CancelRun() => Runner.Cancel();

        private async Task CopyPermalink()
        {
            if (string.IsNullOrWhiteSpace(_hgvs)) return;
            var url = Nav.GetUriWithQueryParameters(Nav.ToAbsoluteUri("").GetLeftPart(UriPartial.Path), new Dictionary<string, object?>
            {
                ["rs"] = null,
                ["hgvs"] = _hgvs.Trim(),
                ["spacer"] = _gRnaSize,
                ["seed"] = $"{_seedStart}-{_seedEnd}",
                // Only written when it differs from the default, so ordinary links stay short.
                ["snp"] = _snpPos == 3 ? null : _snpPos,
                ["mm"] = _mismatchPos == 4 ? null : _mismatchPos
            });
            try
            {
                _linkCopied = await JSRuntime.InvokeAsync<bool>("grnaCopy", url);
            }
            catch (JSDisconnectedException) { }
        }

        // ===== Export =====

        /// <summary>The variants a tab holds: the resolved notations of an rsID, or the single HGVS that was entered.</summary>
        private static IEnumerable<HgvsData> ReportLeaves(InputTabData tab) =>
            tab.Type == InputType.RS
                ? (IEnumerable<HgvsData>?)tab.ChildHgvsList ?? Array.Empty<HgvsData>()
                : tab.DirectHgvs is { } direct ? new[] { direct } : Array.Empty<HgvsData>();

        private static bool HasResults(InputTabData tab) => ReportLeaves(tab).Any(h => h.GRNAs is { Count: > 0 });

        private static void AppendTabRows(StringBuilder sb, InputTabData tab)
        {
            foreach (var hgvs in ReportLeaves(tab))
            {
                GrnaCsvSchema.AppendRows(sb, tab.RsId, hgvs.Hgvs, hgvs.GRNAs, GrnaCsvSchema.MutatedType, hgvs.IsComplement);
                GrnaCsvSchema.AppendRows(sb, tab.RsId, hgvs.Hgvs, hgvs.OriginalGRNAs, GrnaCsvSchema.OriginalType, hgvs.IsComplement);
            }
        }

        /// <summary>One tab's report: the same CSV for an rsID (all its notations) and for a plain HGVS input.</summary>
        private async Task DownloadReport(InputTabData tabData)
        {
            if (!ReportLeaves(tabData).Any()) return;

            var sb = new StringBuilder();
            AppendProvenance(sb);
            sb.AppendLine(GrnaCsvSchema.Header);
            AppendTabRows(sb, tabData);

            var label = tabData.Type == InputType.RS
                ? $"RS{tabData.RsId}"
                : System.Text.RegularExpressions.Regex.Replace(tabData.DisplayLabel, "[^A-Za-z0-9._-]", "_");
            var fileName = $"Report_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, "text/csv;charset=utf-8", sb.ToString());
        }

        /// <summary>Every input in one file, rsIDs and plain HGVS alike.</summary>
        private async Task DownloadAllReports()
        {
            var tabs = _inputTabs.Where(HasResults).ToList();
            if (tabs.Count == 0) return;

            var sb = new StringBuilder();
            AppendProvenance(sb);
            sb.AppendLine(GrnaCsvSchema.Header);
            foreach (var tab in tabs) AppendTabRows(sb, tab);

            var fileName = $"Report_All_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, "text/csv;charset=utf-8", sb.ToString());
        }

        /// <summary>Parameters go in the file so an exported report can be reproduced (leading # lines are ignored by the parser).</summary>
        private void AppendProvenance(StringBuilder sb)
        {
            var p = Runner.Parameters ?? CurrentParameters;
            sb.AppendLine(GrnaCsvSchema.ProvenanceLine(p.SpacerSize, p.SeedStart, p.SeedEnd));
        }

        private bool _oligoT7 = true;
        private bool _oligoPairs = true;
        private bool _fastaRna;

        private async Task DownloadOligos()
        {
            if (StateService.Shortlist.Count == 0) return;
            var csv = OligoExport.ShortlistCsv(StateService.Shortlist, _oligoT7, _oligoPairs, PlateKind.Plate96, StateService.Construct);
            await JSRuntime.InvokeVoidAsync("downloadFile", $"Oligos_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv;charset=utf-8", csv);
        }

        private async Task DownloadFasta()
        {
            if (StateService.Shortlist.Count == 0) return;
            var fasta = OligoExport.ShortlistFasta(StateService.Shortlist, StateService.Construct, _fastaRna);
            await JSRuntime.InvokeVoidAsync("downloadFile", $"Shortlist_{DateTime.Now:yyyyMMdd_HHmmss}.fasta", "text/plain;charset=utf-8", fasta);
        }

        private string? _uploadMessage;

        private async Task OnFileSelected(InputFileChangeEventArgs e)
        {
            _uploadMessage = null;
            try
            {
                using var reader = new StreamReader(e.File.OpenReadStream(maxAllowedSize: 1024 * 1024));
                var text = await reader.ReadToEndAsync();
                var session = SessionDocument.TryParse(text);
                var input = SessionDocument.InputFromUpload(text);
                if (string.IsNullOrWhiteSpace(input))
                {
                    _uploadMessage = "No variants could be read from that file.";
                    return;
                }

                _hgvs = input;
                if (session is not null)
                {
                    if (session.Spacer > 0) _gRnaSize = session.Spacer;
                    var parts = session.Seed.Split('-', 2);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var en)) { _seedStart = s; _seedEnd = en; }
                    StateService.Shortlist = session.Shortlist;
                    if (session.Construct is not null) StateService.Construct = session.Construct;
                }

                _uploadMessage = $"Loaded {AnalysisRunner.ParseInputs(input).Count} input(s) from {e.File.Name}. Press Run analysis.";
                SaveSession();
            }
            catch (Exception ex)
            {
                _uploadMessage = $"Could not read the file: {ex.Message}";
            }
        }

        private async Task DownloadSession()
        {
            var doc = new SessionDocument(SessionDocument.CurrentVersion, _hgvs ?? "", _gRnaSize, $"{_seedStart}-{_seedEnd}", StateService.Shortlist, StateService.Construct);
            await JSRuntime.InvokeVoidAsync("downloadFile", $"Session_{DateTime.Now:yyyyMMdd_HHmmss}.json", "application/json;charset=utf-8", SessionDocument.Serialize(doc));
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
