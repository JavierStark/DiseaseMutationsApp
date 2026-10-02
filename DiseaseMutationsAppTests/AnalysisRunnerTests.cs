using DiseaseMutationsApp.Pages;
using DiseaseMutationsApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiseaseMutationsAppTests;

public class AnalysisRunnerTests
{
    private sealed class FakeAnalysis : IGrnaAnalysis
    {
        public int Current;
        public int MaxObserved;
        public Func<string, CancellationToken, Task>? OnAnalyze;
        public Func<string, Task<List<string>>>? OnResolve;
        public readonly List<string> Analyzed = new();

        public async Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref Current);
            lock (this) { MaxObserved = Math.Max(MaxObserved, now); Analyzed.Add(hgvs); }
            try
            {
                if (OnAnalyze != null) await OnAnalyze(hgvs, cancellationToken);
                else await Task.Delay(40, cancellationToken);
                return new ResultFromHGVS
                {
                    gRNA = new(), OriginalGRNA = new(), MutatedSequence = "ACGT", OriginalSequence = "ACGA", ExtraNucleotids = 1
                };
            }
            finally
            {
                Interlocked.Decrement(ref Current);
            }
        }

        public Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default) =>
            OnResolve != null ? OnResolve(rsid) : Task.FromResult(new List<string> { "NG_1.1:g.1A>T", "NG_1.1:g.2A>T" });

        public string? GetNcbiNuccoreUrl(string hgvs) => "https://example.test/" + hgvs;
    }

    private static (AnalysisRunner Runner, AppStateService State, FakeAnalysis Fake) Create(Action<AnalysisOptions>? configure = null)
    {
        var options = new AnalysisOptions { VariantTimeoutSeconds = 30 };
        configure?.Invoke(options);
        var fake = new FakeAnalysis();
        var state = new AppStateService();
        var runner = new AnalysisRunner(fake, state, Options.Create(options), NullLogger<AnalysisRunner>.Instance);
        return (runner, state, fake);
    }

    private static readonly RunParameters P = new(28, 10, 17);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) Assert.Fail("Timed out waiting for condition.");
            await Task.Delay(10);
        }
    }

    [Test]
    public void ParseInputs_TrimsDedupesAndSplitsOnSeparators()
    {
        var parsed = AnalysisRunner.ParseInputs(" rs1, RS1 ;NG_1:g.1A>T\nNG_1:g.1A>T,, ");
        Assert.That(parsed, Is.EqualTo(new[] { "rs1", "NG_1:g.1A>T" }));
    }

    [TestCase("rs123", true)]
    [TestCase("RS9", true)]
    [TestCase("rs", false)]
    [TestCase("NG_1.1:g.1A>T", false)]
    public void IsRsId_DetectsRsIds(string input, bool expected) =>
        Assert.That(AnalysisRunner.IsRsId(input), Is.EqualTo(expected));

    [Test]
    public async Task Run_AnalysesEveryVariant_WithBoundedConcurrency()
    {
        var (runner, state, fake) = Create(o => o.MaxConcurrentVariants = 2);
        var inputs = string.Join(",", Enumerable.Range(1, 8).Select(i => $"NG_1.1:g.{i}A>T"));

        Assert.That(runner.TryStart(inputs, P), Is.True);
        await runner.WaitForRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(runner.Phase, Is.EqualTo(RunPhase.Completed));
            Assert.That(state.IndexInputTabs.SelectMany(t => t.Leaves).All(l => l.Status == LeafStatus.Ready), Is.True);
            Assert.That(fake.Analyzed, Has.Count.EqualTo(8));
            Assert.That(fake.MaxObserved, Is.LessThanOrEqualTo(2));
            Assert.That(runner.GetProgress(), Is.EqualTo(new RunProgress(8, 8, 0, 0, 0, 0)));
        });
    }

    [Test]
    public async Task Concurrency_IsClampedToTheMaximum()
    {
        var (runner, _, fake) = Create(o => o.MaxConcurrentVariants = 99);
        runner.TryStart(string.Join(",", Enumerable.Range(1, 16).Select(i => $"NG_1.1:g.{i}A>T")), P);
        await runner.WaitForRunAsync();
        Assert.That(fake.MaxObserved, Is.LessThanOrEqualTo(AnalysisOptions.MaxAllowedConcurrency));
    }

    [Test]
    public async Task TryStart_WhileBusy_IsRejected()
    {
        var (runner, _, fake) = Create();
        var gate = new TaskCompletionSource();
        fake.OnAnalyze = async (_, ct) => await gate.Task.WaitAsync(ct);

        Assert.That(runner.TryStart("NG_1.1:g.1A>T", P), Is.True);
        Assert.That(runner.TryStart("NG_1.1:g.2A>T", P), Is.False, "a double-click must not start a second run");

        gate.SetResult();
        await runner.WaitForRunAsync();
        Assert.That(runner.TryStart("NG_1.1:g.2A>T", P), Is.True);
        await runner.WaitForRunAsync();
    }

    [Test]
    public async Task RsInput_ExpandsToNormalAndComplementLeaves()
    {
        var (runner, state, _) = Create();
        runner.TryStart("rs334", P);
        await runner.WaitForRunAsync();

        var tab = state.IndexInputTabs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(tab.IsLoading, Is.False);
            Assert.That(tab.ChildHgvsList, Has.Count.EqualTo(4));
            Assert.That(tab.ChildHgvsList!.Count(l => l.IsComplement), Is.EqualTo(2));
            Assert.That(state.IndexActiveLeafIds[tab.Id], Is.EqualTo(tab.ChildHgvsList![0].Id));
        });
    }

    [Test]
    public async Task DiscoveryFailure_IsReportedOnTheTab_AndOtherInputsStillRun()
    {
        var (runner, state, _) = Create();
        var fake = (FakeAnalysis)typeof(AnalysisRunner).GetField("_grna", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runner)!;
        fake.OnResolve = _ => throw new HttpRequestException("boom");

        runner.TryStart("rs1,NG_1.1:g.1A>T", P);
        await runner.WaitForRunAsync();

        var rs = state.IndexInputTabs.Single(t => t.Type == InputType.RS);
        var hgvs = state.IndexInputTabs.Single(t => t.Type == InputType.HGVS);
        Assert.Multiple(() =>
        {
            Assert.That(rs.ErrorMessage, Does.Contain("boom"));
            Assert.That(hgvs.DirectHgvs!.Status, Is.EqualTo(LeafStatus.Ready));
        });
    }

    [Test]
    public async Task OneFailingVariant_DoesNotAffectTheOthers()
    {
        var (runner, state, fake) = Create();
        fake.OnAnalyze = (h, _) => h.Contains(":g.2") ? throw new InvalidOperationException("bad accession") : Task.CompletedTask;

        runner.TryStart("NG_1.1:g.1A>T,NG_1.1:g.2A>T,NG_1.1:g.3A>T", P);
        await runner.WaitForRunAsync();

        var leaves = state.IndexInputTabs.SelectMany(t => t.Leaves).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(leaves.Count(l => l.Status == LeafStatus.Ready), Is.EqualTo(2));
            Assert.That(leaves.Single(l => l.Status == LeafStatus.Failed).ErrorMessage, Is.EqualTo("bad accession"));
            Assert.That(runner.Phase, Is.EqualTo(RunPhase.Completed));
        });
    }

    [Test]
    public async Task Cancel_StopsTheRun_AndMarksPendingLeavesCancelled()
    {
        var (runner, state, fake) = Create(o => o.MaxConcurrentVariants = 2);
        fake.OnAnalyze = async (_, ct) => await Task.Delay(Timeout.Infinite, ct);

        runner.TryStart(string.Join(",", Enumerable.Range(1, 6).Select(i => $"NG_1.1:g.{i}A>T")), P);
        await WaitUntil(() => fake.Current == 2);
        runner.Cancel();
        await runner.WaitForRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(runner.Phase, Is.EqualTo(RunPhase.Cancelled));
            Assert.That(state.IndexInputTabs.SelectMany(t => t.Leaves).All(l => l.Status == LeafStatus.Cancelled), Is.True);
            Assert.That(fake.Current, Is.EqualTo(0), "no work may keep running after cancel");
            Assert.That(fake.Analyzed, Has.Count.EqualTo(2), "queued variants must never start");
        });
    }

    [Test]
    public async Task CancelLeaf_OnlyAffectsThatVariant()
    {
        var (runner, state, fake) = Create(o => o.MaxConcurrentVariants = 2);
        var release = new TaskCompletionSource();
        fake.OnAnalyze = async (h, ct) =>
        {
            if (h.Contains(":g.1A")) await Task.Delay(Timeout.Infinite, ct);
            else await release.Task.WaitAsync(ct);
        };

        runner.TryStart("NG_1.1:g.1A>T,NG_1.1:g.2A>T,NG_1.1:g.3A>T", P);
        await WaitUntil(() => fake.Current == 2);
        var slow = state.IndexInputTabs.First().DirectHgvs!;
        runner.CancelLeaf(slow.Id);
        release.SetResult();
        await runner.WaitForRunAsync();

        var leaves = state.IndexInputTabs.SelectMany(t => t.Leaves).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(slow.Status, Is.EqualTo(LeafStatus.Cancelled));
            Assert.That(leaves.Count(l => l.Status == LeafStatus.Ready), Is.EqualTo(2));
            Assert.That(runner.Phase, Is.EqualTo(RunPhase.Completed));
        });
    }

    [Test]
    public async Task VariantTimeout_FailsThatVariantWithAMessage()
    {
        var (runner, state, fake) = Create(o => o.VariantTimeoutSeconds = 1);
        fake.OnAnalyze = async (_, ct) => await Task.Delay(Timeout.Infinite, ct);

        runner.TryStart("NG_1.1:g.1A>T", P);
        await runner.WaitForRunAsync();

        var leaf = state.IndexInputTabs.Single().DirectHgvs!;
        Assert.Multiple(() =>
        {
            Assert.That(leaf.Status, Is.EqualTo(LeafStatus.Failed));
            Assert.That(leaf.ErrorMessage, Does.Contain("Timed out"));
        });
    }

    [Test]
    public async Task RetryLeaf_RerunsAFailedVariant()
    {
        var (runner, state, fake) = Create();
        var fail = true;
        fake.OnAnalyze = (_, _) => fail ? throw new InvalidOperationException("x") : Task.CompletedTask;

        runner.TryStart("NG_1.1:g.1A>T", P);
        await runner.WaitForRunAsync();
        var leaf = state.IndexInputTabs.Single().DirectHgvs!;
        Assert.That(leaf.Status, Is.EqualTo(LeafStatus.Failed));

        fail = false;
        Assert.That(runner.TryRetryLeaf(leaf), Is.True);
        await runner.WaitForRunAsync();
        Assert.That(leaf.Status, Is.EqualTo(LeafStatus.Ready));
    }

    [Test]
    public async Task Mutations_AreMarshalledOntoTheAttachedContext()
    {
        var (runner, _, _) = Create();
        var marshalled = 0;
        runner.Attach(action => { Interlocked.Increment(ref marshalled); action(); return Task.CompletedTask; });

        runner.TryStart("NG_1.1:g.1A>T", P);
        await runner.WaitForRunAsync();

        Assert.That(marshalled, Is.GreaterThan(0), "model mutations must go through the attached dispatcher");
    }

    [Test]
    public async Task Changed_IsRaisedForPhaseChanges()
    {
        var (runner, _, _) = Create();
        var raised = 0;
        runner.Changed += () => Interlocked.Increment(ref raised);

        runner.TryStart("NG_1.1:g.1A>T", P);
        await runner.WaitForRunAsync();

        Assert.That(raised, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task Dispose_CancelsInFlightWork()
    {
        var (runner, _, fake) = Create();
        fake.OnAnalyze = async (_, ct) => await Task.Delay(Timeout.Infinite, ct);
        runner.TryStart("NG_1.1:g.1A>T", P);
        await WaitUntil(() => fake.Current == 1);

        await runner.DisposeAsync();

        Assert.That(fake.Current, Is.EqualTo(0));
    }
}
