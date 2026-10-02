using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server. Pages declare no render mode of their own: the router sets it once, with
// prerendering off, so a page never executes its pipeline twice (prerender + circuit).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Stack traces in the browser only in Development.
        options.DetailedErrors = builder.Environment.IsDevelopment();
        // A Wi-Fi blip during a long run should not destroy it, but each retained circuit pins a
        // full result set, so retention is long and the number retained is small.
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
        options.DisconnectedCircuitMaxRetained = 10;
        options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
    })
    .AddHubOptions(options =>
    {
        // Pasting a large Builder CSV into the Pooling textarea exceeds the 32 KB default.
        options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
    });

builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));

// Per-circuit UI state and the run that owns background work across navigation.
builder.Services.AddScoped<AppStateService>();
builder.Services.AddScoped<AnalysisRunner>();
builder.Services.AddScoped<SessionStorageService>();

// Bowtie is serialised per process, so the service is a singleton.
builder.Services.AddSingleton<gRNA.Services.BowtieService>();
builder.Services.AddScoped<GrnaService>();
builder.Services.AddScoped<PoolingService>();
builder.Services.AddSingleton<DiagnosticsService>();

// Configure Kestrel for long-running bioinformatics operations
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

// Route the library's diagnostics through the host logger (it is silent by default).
var libraryLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("gRNA");
gRNA.GrnaLog.setSink(message => libraryLog.LogDebug("{Message}", message));

if (!app.Environment.IsDevelopment())
{
    // A static file: a Blazor-routed error page would try to start a circuit while the server is failing.
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        ExceptionHandler = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(
                app.Environment.WebRootFileProvider.GetFileInfo("error.html"));
        }
    });
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/healthz", async (DiagnosticsService diagnostics) =>
{
    var report = await diagnostics.GetReportAsync(includeNetwork: false);
    return Results.Json(report, statusCode: report.Healthy ? 200 : 503);
});

app.MapRazorComponents<DiseaseMutationsApp.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
