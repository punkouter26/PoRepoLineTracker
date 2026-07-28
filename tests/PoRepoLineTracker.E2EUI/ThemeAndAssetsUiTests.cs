using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 4.3 — system-aware light/dark themes and design tokens exposed as CSS custom properties,
/// plus the static assets the shell needs in order to render at all.
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class ThemeAndAssetsUiTests
{
    private readonly E2EUiFixture _fixture;

    public ThemeAndAssetsUiTests(E2EUiFixture fixture) => _fixture = fixture;

    private async Task<IPage> OpenWithSchemeAsync(string scheme)
    {
        Skip.If(_fixture.Browser is null, "Playwright browser unavailable — run playwright.ps1 install.");

        var context = await _fixture.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = E2EUiFixture.Desktop,
            IgnoreHTTPSErrors = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(E2EUiFixture.BaseUrl.TrimEnd('/') + "/login", new PageGotoOptions { Timeout = 10000 });
        }
        catch (PlaywrightException ex)
        {
            await context.DisposeAsync();
            throw new SkipException($"No app instance reachable at {E2EUiFixture.BaseUrl} ({ex.Message}).");
        }
        return page;
    }

    private static Task<string> BodyBackgroundAsync(IPage page) => page.EvaluateAsync<string>(
        "() => getComputedStyle(document.body).backgroundColor");

    [SkippableFact]
    public async Task LightScheme_Renders()
    {
        var page = await OpenWithSchemeAsync("light");
        await using var _ = page.Context;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        (await BodyBackgroundAsync(page)).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task DarkScheme_Renders()
    {
        var page = await OpenWithSchemeAsync("dark");
        await using var _ = page.Context;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        (await BodyBackgroundAsync(page)).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Theme_RespondsToTheSystemColorScheme()
    {
        // Rule 4.3 — the app must follow the OS preference, not pick one and ignore it.
        var light = await OpenWithSchemeAsync("light");
        await using var _ = light.Context;
        await light.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });
        var lightBackground = await BodyBackgroundAsync(light);

        var dark = await OpenWithSchemeAsync("dark");
        await using var __ = dark.Context;
        await dark.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });
        var darkBackground = await BodyBackgroundAsync(dark);

        darkBackground.Should().NotBe(lightBackground,
            "a system-aware theme must render differently under a dark preference");
    }

    [SkippableFact]
    public async Task DesignTokens_AreExposedAsCssCustomProperties()
    {
        // Rule 4.3 — tokens live in :root custom properties rather than being inlined per element.
        var page = await OpenWithSchemeAsync("light");
        await using var _ = page.Context;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        // Read named tokens rather than enumerating the computed style: Chromium does not list
        // custom properties when iterating CSSStyleDeclaration, so enumeration always reports 0.
        var tokens = await page.EvaluateAsync<string[]>(@"() => {
            const style = getComputedStyle(document.documentElement);
            return ['--primary-color', '--bg-primary', '--text-primary', '--rz-primary']
                .map(name => style.getPropertyValue(name).trim());
        }");

        tokens.Should().NotContain(string.Empty,
            "every design token the stylesheets rely on must resolve at :root");
    }

    [SkippableFact]
    public async Task Stylesheet_LoadsSuccessfully()
    {
        Skip.If(_fixture.Browser is null, "Playwright browser unavailable — run playwright.ps1 install.");

        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        var failures = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Url.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && response.Status >= 400)
                failures.Add($"{response.Status} {response.Url}");
        };

        await page.ReloadAsync(new PageReloadOptions { Timeout = 20000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        failures.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task BlazorRuntime_LoadsSuccessfully()
    {
        Skip.If(_fixture.Browser is null, "Playwright browser unavailable — run playwright.ps1 install.");

        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        var failures = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Url.Contains("_framework", StringComparison.OrdinalIgnoreCase) && response.Status >= 400)
                failures.Add($"{response.Status} {response.Url}");
        };

        await page.ReloadAsync(new PageReloadOptions { Timeout = 20000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        failures.Should().BeEmpty("a failed _framework asset means the WASM app cannot start");
    }

    [SkippableFact]
    public async Task Shell_SetsClickjackingProtection()
    {
        Skip.If(_fixture.Browser is null, "Playwright browser unavailable — run playwright.ps1 install.");

        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        var response = await page.GotoAsync(E2EUiFixture.BaseUrl.TrimEnd('/') + "/login",
            new PageGotoOptions { Timeout = 20000 });

        response!.Headers.Should().ContainKey("x-frame-options");
    }

    [SkippableFact]
    public async Task Reload_KeepsTheUserOnLogin()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        await page.ReloadAsync(new PageReloadOptions { Timeout = 20000 });

        page.Url.Should().Contain("/login", "a reload must not bounce an anonymous visitor elsewhere");
    }

    [SkippableFact]
    public async Task DeepLink_Anonymous_LandsOnLogin()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/settings");
        await using var _ = page.Context;

        await page.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });

        page.Url.Should().Contain("/login");
    }
}
