using DiseaseMutationsApp.Services;
using gRNA;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiseaseMutationsApp.Pages;

/// <summary>
/// Disease variants: a disease name, OMIM number or MONDO id in, the ClinVar variants (rsIDs) of that disease out, and any
/// selection handed to the Diana page. Searches are cancelled when the user leaves the page; the result and the selection
/// are kept in <see cref="AppStateService"/> so coming back does not lose them.
/// </summary>
public partial class DiseaseSearch : ComponentBase, IDisposable
{
    [Inject] private IDiseaseVariantSource Source { get; set; } = default!;
    [Inject] private AppStateService StateService { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>?q= fills the search box and nothing else: a link never starts a search.</summary>
    [SupplyParameterFromQuery(Name = "q")] private string? QueryText { get; set; }

    private enum Busy { None, Diseases, Variants }

    private DiseaseSearchState S => StateService.DiseaseSearch;

    private Busy _busy;
    private CancellationTokenSource? _cts;
    private DiseaseProgressDto? _progress;
    private string? _error;
    private Func<Task>? _retry;
    private string? _notice;
    private bool _linkBanner;
    private string? _copied;
    private bool _disposed;
    private IReadOnlyList<DiseaseVariantDto>? _view;

    private bool IsBusy => _busy != Busy.None;

    // ===== Lifecycle =====

    protected override void OnParametersSet()
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;
        var claim = QueryText.Trim();
        if (S.ClaimedQuery == claim) return;
        S.ClaimedQuery = claim;
        S.Query = claim;
        _linkBanner = true;
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private CancellationToken Begin(Busy kind)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _busy = kind;
        _error = null;
        _retry = null;
        _notice = null;
        _progress = null;
        return _cts.Token;
    }

    private void Cancel() => _cts?.Cancel();

    private async Task Refresh()
    {
        if (_disposed) return;
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    // ===== Step 1: find diseases =====

    private bool CanFindDiseases => !IsBusy && !string.IsNullOrWhiteSpace(S.Query);

    private async Task FindDiseases()
    {
        if (!CanFindDiseases) return;
        _linkBanner = false;
        var ct = Begin(Busy.Diseases);
        var text = S.Query.Trim();
        try
        {
            var matches = await Source.FindDiseasesAsync(text, ct);
            S.Matches = matches;
            S.ResultDisease = null;
            S.Result = null;
            S.Selected.Clear();
            _view = null;
            var first = matches.FirstOrDefault();
            S.SelectedMatchId = first?.Id;
            S.IncludeSubtypes = first?.NeedsSubtypes ?? false;
        }
        catch (OperationCanceledException) { _notice = "Search cancelled."; }
        catch (GrnaInputException ex) { _error = ex.Message; }
        catch (GrnaUpstreamException ex)
        {
            _error = ex.Message;
            _retry = FindDiseases;
        }
        finally
        {
            _busy = Busy.None;
        }
        await Refresh();
    }

    private DiseaseMatchDto? SelectedMatch => S.Matches?.FirstOrDefault(m => m.Id == S.SelectedMatchId);

    private void SelectMatch(DiseaseMatchDto match)
    {
        S.SelectedMatchId = match.Id;
        S.IncludeSubtypes = match.NeedsSubtypes;
    }

    // ===== Step 2: find variants =====

    private bool CanFindVariants => !IsBusy && SelectedMatch is not null;

    private async Task FindVariants()
    {
        if (!CanFindVariants) return;
        var match = SelectedMatch!;
        var includeSubtypes = S.IncludeSubtypes || match.NeedsSubtypes;
        var ct = Begin(Busy.Variants);
        var progress = new RelayProgress(p => { _progress = p; _ = Refresh(); });
        try
        {
            var result = await Source.FindVariantsAsync(match, includeSubtypes, S.Significance, progress, ct);
            S.Result = result;
            S.ResultDisease = match;
            S.Selected.Clear();
            S.FilterText = "";
            S.OnlyWithRsId = false;
            S.Page = 0;
            S.SortColumn = DiseaseSortColumn.None;
            _view = null;
        }
        catch (OperationCanceledException) { _notice = "Search cancelled."; }
        catch (GrnaInputException ex) { _error = ex.Message; }
        catch (GrnaUpstreamException ex)
        {
            _error = ex.Message;
            _retry = FindVariants;
        }
        finally
        {
            _busy = Busy.None;
            _progress = null;
        }
        await Refresh();
    }

    private sealed class RelayProgress : IProgress<DiseaseProgressDto>
    {
        private readonly Action<DiseaseProgressDto> _on;
        public RelayProgress(Action<DiseaseProgressDto> on) => _on = on;
        public void Report(DiseaseProgressDto value) => _on(value);
    }

    // ===== Table: filter, sort, page =====

    private IReadOnlyList<DiseaseVariantDto> AllVariants => S.Result?.Variants ?? Array.Empty<DiseaseVariantDto>();

    private IReadOnlyList<DiseaseVariantDto> View
    {
        get
        {
            if (_view is not null) return _view;
            IEnumerable<DiseaseVariantDto> rows = AllVariants;
            if (S.OnlyWithRsId) rows = rows.Where(v => v.RsId is not null);
            var text = S.FilterText.Trim();
            if (text.Length > 0)
                rows = rows.Where(v => Matches(v, text));
            rows = Sort(rows);
            return _view = rows.ToList();
        }
    }

    private static bool Matches(DiseaseVariantDto v, string text)
    {
        bool Has(string? field) => field is not null && field.Contains(text, StringComparison.OrdinalIgnoreCase);
        return Has(v.RsId) || Has(v.Hgvs) || Has(v.Gene) || Has(v.Name) || Has(v.Significance) || v.Conditions.Any(c => Has(c));
    }

    private IEnumerable<DiseaseVariantDto> Sort(IEnumerable<DiseaseVariantDto> rows)
    {
        Func<DiseaseVariantDto, object?>? key = S.SortColumn switch
        {
            DiseaseSortColumn.Variant => v => v.DianaInput ?? "",
            DiseaseSortColumn.Gene => v => v.Gene ?? "",
            DiseaseSortColumn.Name => v => v.Name,
            DiseaseSortColumn.Classification => v => v.Significance,
            DiseaseSortColumn.Stars => v => v.Stars,
            _ => null
        };
        if (key is null) return rows;
        return S.SortAscending
            ? rows.OrderBy(key, Comparer<object?>.Create(CompareKeys))
            : rows.OrderByDescending(key, Comparer<object?>.Create(CompareKeys));
    }

    private static int CompareKeys(object? a, object? b) => a is int x && b is int y
        ? x.CompareTo(y)
        : string.Compare(a as string, b as string, StringComparison.OrdinalIgnoreCase);

    private int PageCount => Math.Max(1, (View.Count + S.PageSize - 1) / S.PageSize);

    private IEnumerable<DiseaseVariantDto> PageItems
    {
        get
        {
            var page = Math.Clamp(S.Page, 0, PageCount - 1);
            return View.Skip(page * S.PageSize).Take(S.PageSize);
        }
    }

    private bool FilterActive => S.OnlyWithRsId || S.FilterText.Trim().Length > 0;

    private void FilterChanged()
    {
        S.Page = 0;
        _view = null;
    }

    private void ClearFilters()
    {
        S.FilterText = "";
        S.OnlyWithRsId = false;
        FilterChanged();
    }

    private void SortBy(DiseaseSortColumn column)
    {
        if (S.SortColumn == column) S.SortAscending = !S.SortAscending;
        else { S.SortColumn = column; S.SortAscending = true; }
        S.Page = 0;
        _view = null;
    }

    private void PageSizeChanged() => S.Page = 0;

    // ===== Selection =====

    private bool IsSelectable(DiseaseVariantDto v) => v.DianaInput is not null;

    private void ToggleRow(DiseaseVariantDto v, bool on)
    {
        if (!IsSelectable(v)) return;
        if (on) S.Selected.Add(v.ClinVarId); else S.Selected.Remove(v.ClinVarId);
        _copied = null;
    }

    /// <summary>Selects every row that matches the current filter, on every page.</summary>
    private void SelectAllMatching()
    {
        foreach (var v in View.Where(IsSelectable)) S.Selected.Add(v.ClinVarId);
        _copied = null;
    }

    private void ClearSelection()
    {
        S.Selected.Clear();
        _copied = null;
    }

    private IEnumerable<DiseaseVariantDto> SelectedVariants => AllVariants.Where(v => S.Selected.Contains(v.ClinVarId));

    /// <summary>What would be sent: distinct rsIDs, or genomic HGVS for rows without an rsID.</summary>
    private List<string> SelectedInputs => DiseaseExport.DianaInputs(SelectedVariants);

    private int SelectableMatching => View.Count(IsSelectable);

    // ===== Hand-off to Diana =====

    private int Limit => AnalysisRunner.MaxInputs;

    /// <summary>Null when the send is allowed, otherwise why not.</summary>
    private string? SendBlockedReason(bool append, int count)
    {
        if (count == 0) return null;
        var total = append
            ? AnalysisRunner.ParseInputs(StateService.IndexHgvsInput).Concat(SelectedInputs).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : count;
        if (total <= Limit) return null;
        return append
            ? $"The Diana box would hold {total} inputs; it takes at most {Limit} per run. Select fewer, or replace its content."
            : $"Diana takes at most {Limit} inputs per run and {count} are selected. Select fewer, or download the list for `grna design --input`.";
    }

    private void Send(bool append)
    {
        var inputs = SelectedInputs;
        if (inputs.Count == 0 || SendBlockedReason(append, inputs.Count) is not null) return;
        StateService.SendToBuilder(inputs, append);
        Nav.NavigateTo("");
    }

    private void SendOne(DiseaseVariantDto v)
    {
        if (v.DianaInput is not { } input) return;
        StateService.SendToBuilder(new[] { input }, append: true);
        Nav.NavigateTo("");
    }

    // ===== Copy and download =====

    private async Task CopySelected()
    {
        try
        {
            var ok = await JS.InvokeAsync<bool>("grnaCopy", string.Join("\n", SelectedInputs));
            _copied = ok ? $"Copied {SelectedInputs.Count} to the clipboard." : "Could not copy to the clipboard.";
        }
        catch (JSDisconnectedException) { }
    }

    private string Stem => DiseaseExport.FileStem(S.ResultDisease?.Name ?? "disease");

    private async Task DownloadSelected()
    {
        var text = DiseaseExport.InputList(SelectedVariants);
        if (text.Length == 0) return;
        await Download($"Disease_{Stem}_selected_{DateTime.Now:yyyyMMdd_HHmmss}.txt", "text/plain;charset=utf-8", text);
    }

    private async Task DownloadAllRsIds()
    {
        var text = DiseaseExport.RsList(AllVariants);
        if (text.Length == 0) return;
        await Download($"Disease_{Stem}_rsids_{DateTime.Now:yyyyMMdd_HHmmss}.txt", "text/plain;charset=utf-8", text);
    }

    private async Task DownloadTable() =>
        await Download($"Disease_{Stem}_variants_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv;charset=utf-8", DiseaseExport.Csv(AllVariants));

    private async Task Download(string fileName, string contentType, string content)
    {
        try { await JS.InvokeVoidAsync("downloadFile", fileName, contentType, content); }
        catch (JSDisconnectedException) { }
    }

    // ===== Display helpers =====

    private static string Stars(int n) => new string('★', Math.Clamp(n, 0, 4)) + new string('☆', 4 - Math.Clamp(n, 0, 4));

    private static string DbSnpUrl(string rs) => $"https://www.ncbi.nlm.nih.gov/snp/{Uri.EscapeDataString(rs)}";
    private static string ClinVarUrl(string id) => $"https://www.ncbi.nlm.nih.gov/clinvar/variation/{Uri.EscapeDataString(id)}/";

    private static string Describe(DiseaseMatchDto m) =>
        m.OmimNumbers.Count > 0 ? "OMIM " + string.Join(", ", m.OmimNumbers)
        : m.PhenotypicSeries.Count > 0 ? "phenotypic series " + string.Join(", ", m.PhenotypicSeries) + ": variants come from its subtypes"
        : "no OMIM number listed: its subtypes will be consulted";

    private string SignificanceLabel => S.Significance switch
    {
        DiseaseSignificance.PathogenicOnly => "pathogenic",
        DiseaseSignificance.Any => "",
        _ => "pathogenic or likely pathogenic"
    };
}
