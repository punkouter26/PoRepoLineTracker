using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Rule 2.2 — the UI tier must cover mobile and desktop. Rule 4.4 adds the performance angle:
/// the page must not scroll horizontally on a phone, which is the usual symptom of a fixed-width
/// element left over from a desktop-only layout.
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class ResponsiveLayoutUiTests
{
    private readonly E2EUiFixture _fixture;

    public ResponsiveLayoutUiTests(E2EUiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Desktop_RendersTheShell()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        // Wait for a rendered control, not for <body>: the body element exists in index.html
        // before the WASM runtime has rendered anything, so asserting on it races the boot.
        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        (await page.InnerTextAsync("body")).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Mobile_RendersTheShell()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/login");
        await using var _ = page.Context;

        // Wait for a rendered control, not for <body>: the body element exists in index.html
        // before the WASM runtime has rendered anything, so asserting on it races the boot.
        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        (await page.InnerTextAsync("body")).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Mobile_DoesNotScrollHorizontally()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/login");
        await using var _ = page.Context;

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1, "content must fit the phone viewport");
    }

    [SkippableFact]
    public async Task Desktop_DoesNotScrollHorizontally()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20000 });

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1);
    }

    [SkippableFact]
    public async Task Mobile_TapTargetsAreLargeEnough()
    {
        // WCAG 2.2 AA, 2.5.8 Target Size (Minimum) — 24x24 CSS pixels.
        var page = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/login");
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        var undersized = await page.EvaluateAsync<int>(@"() =>
            [...document.querySelectorAll('button, a')]
                .filter(el => el.offsetParent !== null)
                .map(el => el.getBoundingClientRect())
                .filter(r => r.width > 0 && r.height > 0 && (r.width < 24 || r.height < 24))
                .length");

        undersized.Should().Be(0, "every visible control must meet the 24x24 minimum target size");
    }

    [SkippableFact]
    public async Task ViewportMetaTag_IsPresent()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/login");
        await using var _ = page.Context;

        var content = await page.GetAttributeAsync("meta[name=viewport]", "content");

        content.Should().NotBeNullOrWhiteSpace("without it mobile browsers render at desktop width");
    }

    [SkippableFact]
    public async Task Mobile_RendersTheSameRouteAsDesktop()
    {
        var mobile = await _fixture.OpenAsync(E2EUiFixture.Mobile, "/repositories");
        await using var _ = mobile.Context;
        await mobile.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });

        var desktop = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/repositories");
        await using var __ = desktop.Context;
        await desktop.WaitForURLAsync("**/login", new PageWaitForURLOptions { Timeout = 20000 });

        new Uri(mobile.Url).AbsolutePath.Should().Be(new Uri(desktop.Url).AbsolutePath,
            "routing must not depend on viewport width");
    }
}
