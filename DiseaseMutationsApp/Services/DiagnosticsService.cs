using gRNA;

namespace DiseaseMutationsApp.Services;

public record DiagnosticCheck(string Name, string Status, string Detail, string Remedy);

public record DiagnosticsReport(bool Healthy, DateTimeOffset CheckedAt, IReadOnlyList<DiagnosticCheck> Checks);

/// <summary>Runs the library's preflight checks (shared with `grna doctor`) with a short cache.</summary>
public class DiagnosticsService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DiagnosticsReport? _cached;
    private bool _cachedNetwork;

    public async Task<DiagnosticsReport> GetReportAsync(bool includeNetwork, bool forceRefresh = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!forceRefresh && _cached is not null
                && (_cachedNetwork || !includeNetwork)
                && DateTimeOffset.UtcNow - _cached.CheckedAt < CacheFor)
            {
                return _cached;
            }

            var env = GrnaEnvironmentModule.getCurrent();
            var results = await Diagnostics.runAll(env, includeNetwork);
            var checks = results
                .Select(r => new DiagnosticCheck(r.Name, r.Status.IsPass ? "pass" : r.Status.IsWarn ? "warn" : "fail", r.Detail, r.Remedy))
                .ToList();

            _cached = new DiagnosticsReport(Diagnostics.isHealthy(results), DateTimeOffset.UtcNow, checks);
            _cachedNetwork = includeNetwork;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
