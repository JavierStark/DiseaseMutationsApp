using System.Text.RegularExpressions;
using DiseaseMutationsApp.Pages;
using Microsoft.Extensions.Options;

namespace DiseaseMutationsApp.Services;

/// <summary>Tunables bound from the "Analysis" configuration section (e.g. Analysis__MaxConcurrentVariants).</summary>
public class AnalysisOptions
{
    public const int MaxAllowedConcurrency = 4;

    /// <summary>Variants analysed at once. Bowtie is serialised globally, so 2 is enough to hide fetch/fold latency.</summary>
    public int MaxConcurrentVariants { get; set; } = 2;

    /// <summary>rsID to HGVS lookups are pure HTTP, so they get their own, higher bound.</summary>
    public int MaxConcurrentDiscovery { get; set; } = 4;

    public int VariantTimeoutSeconds { get; set; } = 600;

    public int EffectiveVariantConcurrency => Math.Clamp(MaxConcurrentVariants, 1, MaxAllowedConcurrency);
    public int EffectiveDiscoveryConcurrency => Math.Clamp(MaxConcurrentDiscovery, 1, 8);
}

public enum RunPhase { Idle, Discovering, Analyzing, Completed, Cancelled }

/// <summary>Spacer/seed parameters a run was started with.</summary>
public record RunParameters(int SpacerSize, int SeedStart, int SeedEnd);

public record RunProgress(int Total, int Done, int Failed, int Running, int Queued, int Cancelled)
{
    public int Finished => Done + Failed + Cancelled;
}

/// <summary>
/// Owns one analysis run per circuit: the cancellation source, the task, per-variant timeouts and a run id.
/// Scoped, so it survives navigation between pages and dies with the circuit.
///
/// Thread-safety rule: every mutation of the shared model happens through <see cref="Apply"/>, which is
/// marshalled onto the attached component's renderer context, and collections are only ever replaced
/// wholesale. Workers never touch the model directly, so a render can never observe a torn leaf or a
/// list being modified.
/// </summary>
public sealed partial class AnalysisRunner : IAsyncDisposable
{
    private static readonly Regex RsPattern = RsRegex();

    private readonly GrnaService _grna;
    private readonly AppStateService _state;
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisRunner> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _leafCts = new();

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private Func<Action, Task>? _marshal;
    private bool _disposed;
    private int _notifyScheduled;

    public AnalysisRunner(GrnaService grna, AppStateService state, IOptions<AnalysisOptions> options, ILogger<AnalysisRunner> logger)
    {
        _grna = grna;
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    public RunPhase Phase { get; private set; } = RunPhase.Idle;
    public int RunId { get; private set; }
    public RunParameters? Parameters { get; private set; }
    public bool IsBusy => Phase is RunPhase.Discovering or RunPhase.Analyzing;

    /// <summary>Raised (coalesced) whenever visible state changed. Handlers must marshal to their own context.</summary>
    public event Action? Changed;

    /// <summary>Attaches the component whose renderer context model mutations should run on.</summary>
    public void Attach(Func<Action, Task> marshal)
    {
        lock (_gate) _marshal = marshal;
    }

    public void Detach(Func<Action, Task> marshal)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_marshal, marshal)) _marshal = null;
        }
    }

    /// <summary>Progress derived from the model, so every render sees a self-consistent snapshot.</summary>
    public RunProgress GetProgress()
    {
        int total = 0, done = 0, failed = 0, running = 0, queued = 0, cancelled = 0;
        foreach (var tab in _state.IndexInputTabs)
        {
            foreach (var leaf in tab.Leaves)
            {
                total++;
                switch (leaf.Status)
                {
                    case LeafStatus.Ready: done++; break;
                    case LeafStatus.Failed: failed++; break;
                    case LeafStatus.Running: running++; break;
                    case LeafStatus.Queued: queued++; break;
                    case LeafStatus.Cancelled: cancelled++; break;
                }
            }
        }

        return new RunProgress(total, done, failed, running, queued, cancelled);
    }

    /// <summary>Splits the input box into distinct, trimmed entries.</summary>
    public static List<string> ParseInputs(string? raw) =>
        (raw ?? "")
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsRsId(string input) => RsPattern.IsMatch(input);

    /// <summary>Starts a run. Returns false (and does nothing) when one is already in flight.</summary>
    public bool TryStart(string? rawInput, RunParameters parameters)
    {
        lock (_gate)
        {
            if (_disposed || IsBusy) return false;
            var inputs = ParseInputs(rawInput);
            if (inputs.Count == 0) return false;

            var runId = ++RunId;
            Parameters = parameters;
            Phase = RunPhase.Discovering;
            var cts = _runCts = new CancellationTokenSource();
            _leafCts.Clear();

            // Discovery phase 1: the tab list is built complete and published by one assignment.
            _state.IndexInputTabs = inputs.Select(BuildTab).ToList();
            _state.IndexActiveTabIndex = 0;
            _state.IndexActiveLeafIds = new();
            _runTask = Task.Run(() => RunAsync(parameters, runId, cts.Token));
        }

        Notify(immediate: true);
        return true;
    }

    private static InputTabData BuildTab(string input) =>
        IsRsId(input)
            ? new InputTabData { Type = InputType.RS, DisplayLabel = input.ToLowerInvariant(), RsId = input[2..], IsLoading = true }
            : new InputTabData
            {
                Type = InputType.HGVS,
                DisplayLabel = input,
                IsLoading = true,
                DirectHgvs = new HgvsData { Hgvs = input }
            };

    /// <summary>Cancels the whole run.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_gate) cts = _runCts;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Cancels one variant (e.g. a pathological accession starving the Bowtie queue).</summary>
    public void CancelLeaf(Guid leafId)
    {
        CancellationTokenSource? cts;
        lock (_gate) _leafCts.TryGetValue(leafId, out cts);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Re-runs a single failed or cancelled variant while no run is in flight.</summary>
    public bool TryRetryLeaf(HgvsData leaf)
    {
        lock (_gate)
        {
            if (_disposed || IsBusy || Parameters is null) return false;
            var p = Parameters;
            var runId = ++RunId;
            Phase = RunPhase.Analyzing;
            var cts = _runCts = new CancellationTokenSource();
            leaf.ResetForRun();
            _runTask = Task.Run(async () =>
            {
                try { await AnalyzeLeafAsync(leaf, p, runId, cts.Token); }
                catch (OperationCanceledException) { }
                finally { Finish(runId, cts.Token); }
            });
        }

        Notify(immediate: true);
        return true;
    }

    private async Task RunAsync(RunParameters p, int runId, CancellationToken token)
    {
        try
        {
            // Discovery: rsID -> HGVS (pure HTTP, bounded separately).
            var rsTabs = _state.IndexInputTabs.Where(t => t.Type == InputType.RS).ToList();
            await Parallel.ForEachAsync(rsTabs,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EffectiveDiscoveryConcurrency, CancellationToken = token },
                async (tab, ct) => await DiscoverAsync(tab, runId, ct));

            // Analyze: the leaf set is now fixed.
            Apply(runId, () => Phase = RunPhase.Analyzing, immediate: true);
            var leaves = _state.IndexInputTabs.SelectMany(t => t.Leaves).ToList();
            await Parallel.ForEachAsync(leaves,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EffectiveVariantConcurrency, CancellationToken = token },
                async (leaf, ct) => await AnalyzeLeafAsync(leaf, p, runId, ct));
        }
        catch (OperationCanceledException)
        {
            // Run-level cancel: whatever is still pending is marked in Finish.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis run {RunId} failed unexpectedly", runId);
        }
        finally
        {
            Finish(runId, token);
        }
    }

    private void Finish(int runId, CancellationToken token)
    {
        Apply(runId, () =>
        {
            foreach (var tab in _state.IndexInputTabs)
            {
                if (tab.IsLoading) tab.IsLoading = false;
                foreach (var leaf in tab.Leaves.Where(l => l.IsLoading))
                    leaf.Status = LeafStatus.Cancelled;
            }

            Phase = token.IsCancellationRequested ? RunPhase.Cancelled : RunPhase.Completed;
        }, immediate: true);
    }

    private async Task DiscoverAsync(InputTabData tab, int runId, CancellationToken ct)
    {
        try
        {
            var hgvsList = await _grna.GetHgvsFromSnp(tab.RsId!, ct);
            // Complete child list, built off-model and published by a single assignment.
            var children = hgvsList
                .SelectMany(h => new[]
                {
                    new HgvsData { Hgvs = h, IsComplement = false },
                    new HgvsData { Hgvs = h, IsComplement = true }
                })
                .ToList();
            Apply(runId, () =>
            {
                tab.ChildHgvsList = children;
                tab.IsLoading = false;
                if (children.Count > 0) _state.IndexActiveLeafIds[tab.Id] = children[0].Id;
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery failed for rs{Rs}", tab.RsId);
            Apply(runId, () =>
            {
                tab.ErrorMessage = $"Could not look up rs{tab.RsId}: {ex.Message}";
                tab.IsLoading = false;
            });
        }
    }

    private async Task AnalyzeLeafAsync(HgvsData leaf, RunParameters p, int runId, CancellationToken runToken)
    {
        using var leafCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        leafCts.CancelAfter(TimeSpan.FromSeconds(_options.VariantTimeoutSeconds));
        lock (_gate) _leafCts[leaf.Id] = leafCts;

        try
        {
            leafCts.Token.ThrowIfCancellationRequested();
            Apply(runId, () => leaf.Status = LeafStatus.Running);
            var sourceUrl = _grna.GetNcbiNuccoreUrl(leaf.Hgvs);
            var result = await _grna.GetBestgRNAFromHgvs(leaf.Hgvs, p.SpacerSize, p.SeedStart, p.SeedEnd, leaf.IsComplement, leafCts.Token);

            // One atomic publish: every field of the finished leaf lands in a single marshalled step.
            Apply(runId, () =>
            {
                leaf.SourceUrl = sourceUrl;
                leaf.Original = result.OriginalSequence;
                leaf.Mutated = result.MutatedSequence;
                leaf.GRNAs = result.gRNA;
                leaf.OriginalGRNAs = result.OriginalGRNA;
                leaf.ExtraNucleotids = result.ExtraNucleotids;
                leaf.ErrorMessage = null;
                leaf.Status = LeafStatus.Ready;
            });
        }
        catch (OperationCanceledException)
        {
            var timedOut = !runToken.IsCancellationRequested && leafCts.IsCancellationRequested;
            Apply(runId, () =>
            {
                leaf.Status = timedOut ? LeafStatus.Failed : LeafStatus.Cancelled;
                if (timedOut) leaf.ErrorMessage = $"Timed out after {_options.VariantTimeoutSeconds} s.";
            });
            // A cancelled single leaf must not abort its siblings; only a run-level cancel propagates.
            if (runToken.IsCancellationRequested) throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Variant {Hgvs} failed", leaf.Hgvs);
            Apply(runId, () =>
            {
                leaf.ErrorMessage = ex.Message;
                leaf.Status = LeafStatus.Failed;
            });
        }
        finally
        {
            lock (_gate) _leafCts.Remove(leaf.Id);
        }
    }

    /// <summary>
    /// Runs a model mutation on the attached component's context. Stale continuations from an
    /// older run (different run id) become no-ops.
    /// </summary>
    private void Apply(int runId, Action mutation, bool immediate = false)
    {
        Func<Action, Task>? marshal;
        lock (_gate)
        {
            if (runId != RunId || _disposed) return;
            marshal = _marshal;
        }

        void Guarded()
        {
            if (runId != RunId) return;
            mutation();
        }

        try
        {
            if (marshal is null) Guarded();
            else marshal(Guarded).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or TaskCanceledException or OperationCanceledException)
        {
            // Component went away mid-update: apply directly, there is nothing left to render.
            Guarded();
        }

        Notify(immediate);
    }

    /// <summary>Coalesces change notifications (~150 ms) with an immediate flush on phase changes.</summary>
    private void Notify(bool immediate)
    {
        if (immediate)
        {
            Volatile.Write(ref _notifyScheduled, 0);
            RaiseChanged();
            return;
        }

        if (Interlocked.Exchange(ref _notifyScheduled, 1) == 1) return;
        _ = Task.Delay(150).ContinueWith(_ =>
        {
            if (Interlocked.Exchange(ref _notifyScheduled, 0) == 1) RaiseChanged();
        });
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) when (ex is ObjectDisposedException or TaskCanceledException) { }
    }

    /// <summary>Waits for the current run to finish.</summary>
    public Task WaitForRunAsync() => _runTask ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            _disposed = true;
            cts = _runCts;
            task = _runTask;
            _marshal = null;
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* shutting down */ }
        }

        cts?.Dispose();
    }

    [GeneratedRegex(@"^rs\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex RsRegex();
}
