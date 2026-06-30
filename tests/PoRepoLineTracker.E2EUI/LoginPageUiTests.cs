using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 2.2 — C# Playwright UI E2E (replaces the legacy TypeScript suite). Targets E2E_BASE_URL
/// (default http://localhost:5000). Skips (never fails) when the Playwright browsers are not
/// installed (run: pwsh bin/Debug/net10.0/playwright.ps1 install) or no instance is reachable.
/// </summary>
public sealed class LoginPageUiTests : IAsyncLifetime
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync();
        }
        catch (Exception)
        {
            _browser = null; // browsers not installed — tests below skip
        }
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    [SkippableFact]
    public async Task Unauthenticated_Visit_Redirects_To_Login()
    {
        Skip.If(_browser is null, "Playwright browser unavailable — run playwright.ps1 install.");

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync(BaseUrl, new PageGotoOptions { Timeout = 5000 });
        }
        catch (PlaywrightException ex)
        {
            throw new SkipException($"No app instance reachable at {BaseUrl} ({ex.Message}).");
        }

        await page.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });
        Assert.Contains("/login", page.Url);
    }
}
