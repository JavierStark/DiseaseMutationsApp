using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DiseaseMutationsApp.E2E;

/// <summary>
/// Serves the REAL web application (same components, services, JavaScript and CSS) on a loopback port with the analysis
/// pipeline replaced by <see cref="FakeAnalysis"/>, so browser tests are fast and need no Bowtie, ViennaRNA or NCBI.
/// The service wiring mirrors DiseaseMutationsApp/Program.cs; keep the two in step.
/// </summary>
public sealed class E2EHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private E2EHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<E2EHost> StartAsync()
    {
        var appDir = FindAppDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = appDir,
            WebRootPath = Path.Combine(appDir, "wwwroot"),
            ApplicationName = typeof(DiseaseMutationsApp.App).Assembly.GetName().Name,
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ServerLog());
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(o => o.DetailedErrors = true)   // the exception text reaches the browser console
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

        builder.Services.Configure<AnalysisOptions>(o => { o.VariantTimeoutSeconds = 120; });
        builder.Services.AddScoped<AppStateService>();
        builder.Services.AddScoped<AnalysisRunner>();
        builder.Services.AddScoped<SessionStorageService>();
        builder.Services.AddSingleton<gRNA.Services.BowtieService>();
        builder.Services.AddScoped<GrnaService>();
        builder.Services.AddScoped<IGrnaAnalysis, FakeAnalysis>();   // the one deliberate difference from Program.cs
        builder.Services.AddScoped<PoolingService>();
        builder.Services.AddScoped<IDiseaseVariantSource, FakeDiseaseSource>();   // no Monarch or NCBI in browser tests
        builder.Services.AddSingleton<DiagnosticsService>();

        var app = builder.Build();
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<DiseaseMutationsApp.App>().AddInteractiveServerRenderMode();

        await app.StartAsync();
        var url = app.Urls.First();
        return new E2EHost(app, url);
    }

    /// <summary>The application project folder (it holds wwwroot), found by walking up from the test binaries.</summary>
    private static string FindAppDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "DiseaseMutationsApp", "wwwroot");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "DiseaseMutationsApp");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find DiseaseMutationsApp/wwwroot above " + AppContext.BaseDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
