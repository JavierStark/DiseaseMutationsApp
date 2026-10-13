using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace DiseaseMutationsApp.E2E;

/// <summary>
/// Starts the application once (<see cref="E2EHost"/>) and Chromium once, and gives every test a fresh browser context
/// (so no saved theme, session or permalink state leaks between tests). When Chromium is not installed the whole suite
/// is skipped with the command to install it, so `dotnet test` on the solution stays green on a machine without browsers.
/// </summary>
[SetUpFixture]
public class E2EEnvironment
{
    public static E2EHost Host { get; private set; } = null!;
    public static IPlaywright Playwright { get; private set; } = null!;
    public static IBrowser? Browser { get; private set; }
    public static string? SkipReason { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        Host = await E2EHost.StartAsync();
        // A Debug-built circuit with a headless browser can take several seconds per run; the default 5 s is too tight.
        Microsoft.Playwright.Assertions.SetDefaultExpectTimeout(30000);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        try
        {
            Browser = await LaunchAsync();
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            SkipReason = "Chromium is not installed for this Playwright version. Install it once with: " +
                         "set E2E_INSTALL_BROWSERS=1 and run `dotnet test DiseaseMutationsApp.E2E` again.";
        }
    }

    private static async Task<IBrowser> LaunchAsync()
    {
        try
        {
            return await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                                             && Environment.GetEnvironmentVariable("E2E_INSTALL_BROWSERS") == "1")
        {
            // Explicit opt-in only: this downloads Chromium (about 150 MB) into the Playwright cache.
            var exit = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exit != 0) throw new InvalidOperationException("playwright install chromium failed with exit code " + exit);
            return await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        Playwright?.Dispose();
        if (Host is not null) await Host.DisposeAsync();
    }
}

public abstract class E2EBase
{
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    /// <summary>Uncaught JavaScript errors and console errors seen by the page; a test fails if any appear.</summary>
    protected List<string> BrowserErrors { get; } = new();
    protected static string BaseUrl => E2EEnvironment.Host.BaseUrl;

    /// <summary>Override to emulate another screen, for example a phone.</summary>
    protected virtual BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        AcceptDownloads = true,
        Locale = "en-US"
    };

    [SetUp]
    public async Task OpenPageAsync()
    {
        if (E2EEnvironment.SkipReason is { } reason) Assert.Ignore(reason);
        Context = await E2EEnvironment.Browser!.NewContextAsync(ContextOptions());
        Page = await Context.NewPageAsync();
        Page.SetDefaultTimeout(20000);
        Page.PageError += (_, message) => BrowserErrors.Add("pageerror: " + message);
        Page.Console += (_, m) => { if (m.Type == "error") BrowserErrors.Add("console.error: " + m.Text); };
    }

    [TearDown]
    public async Task ClosePageAsync()
    {
        if (Page is null) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "e2e-failures");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, TestContext.CurrentContext.Test.Name.Replace('"', '_').Replace('/', '_').Replace(':', '_') + ".png");
            try { await Page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = true }); } catch { /* best effort */ }
            TestContext.AddTestAttachment(file);
        }
        await Context.CloseAsync();
        foreach (var e in BrowserErrors) TestContext.Out.WriteLine("[browser] " + e);
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
            foreach (var e in ServerLog.Entries.TakeLast(5)) TestContext.Out.WriteLine(e);
        if (BrowserErrors.Count > 0 && TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Failed)
            Assert.Fail("The browser reported errors:" + Environment.NewLine + string.Join(Environment.NewLine, BrowserErrors));
    }

    protected async Task GotoAsync(string path = "/")
    {
        await Page.GotoAsync(BaseUrl + path);
        // The interactive circuit is ready once the app's own heading has focus (FocusOnNavigate) or the first input accepts text.
        await Expect(Page.Locator("h1")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => window.Blazor !== undefined");
        await Page.WaitForTimeoutAsync(300);
    }

    // ---- shared locators ---------------------------------------------------------------------------

    protected ILocator VariantsBox => Page.GetByLabel("HGVS or rs codes", new() { Exact = false });
    protected ILocator RunButton => Page.GetByRole(AriaRole.Button, new() { Name = "Run analysis" });
    protected ILocator CancelRunButton => Page.GetByRole(AriaRole.Button, new() { Name = "Cancel run" });
    protected ILocator DoneStatus => Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Done:" }).First;

    protected async Task RunAsync(string variants)
    {
        await VariantsBox.FillAsync(variants);
        await Expect(RunButton).ToBeEnabledAsync();
        await RunButton.ClickAsync();
    }
}

public static class AxeHelper
{
    /// <summary>Runs axe-core (WCAG 2.0 to 2.2 A and AA rules) on the current page and returns the violations.</summary>
    public static async Task<List<Deque.AxeCore.Commons.AxeResultItem>> ViolationsAsync(this IPage page)
    {
        var results = await Deque.AxeCore.Playwright.PageExtensions.RunAxe(page, new Deque.AxeCore.Commons.AxeRunOptions
        {
            RunOnly = new Deque.AxeCore.Commons.RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa" }
            }
        });
        return results.Violations.ToList();
    }
}
