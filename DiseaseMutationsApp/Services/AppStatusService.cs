using gRNA;

namespace DiseaseMutationsApp.Services;

public record AppStatusCheck(string Name, string Status, string Detail, string Remedy);

public record AppStatusReport(bool Healthy, DateTimeOffset CheckedAt, IReadOnlyList<AppStatusCheck> Checks);

/// <summary>Runs the library's preflight checks (shared with `grna doctor`) with a short cache.</summary>
public class AppStatusService
{
    /// <summary>
    /// What /healthz returns. Loopback callers get the full report; anyone else only the verdict and each check's
    /// name and status, because the detail and remedy texts contain file-system paths of the host.
    /// </summary>
    public static object HealthzPayload(AppStatusReport report, bool isLoopback) =>
        isLoopback
            ? report
            : new
            {
                healthy = report.Healthy,
                checkedAt = report.CheckedAt,
                checks = report.Checks.Select(c => new { name = c.Name, status = c.Status }).ToList()
            };

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppStatusReport? _cached;
    private bool _cachedNetwork;

    public async Task<AppStatusReport> GetReportAsync(bool includeNetwork, bool forceRefresh = false)
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
                .Select(r => new AppStatusCheck(r.Name, r.Status.IsPass ? "pass" : r.Status.IsWarn ? "warn" : "fail", r.Detail, r.Remedy))
                .ToList();

            _cached = new AppStatusReport(Diagnostics.isHealthy(results), DateTimeOffset.UtcNow, checks);
            _cachedNetwork = includeNetwork;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
