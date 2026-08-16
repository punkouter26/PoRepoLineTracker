using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// The UI tier must cover mobile and desktop. There is also a performance angle:
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

    /// <summary>
    /// The routes worth checking at phone width. Every test above this point loads <c>/login</c>,
    /// which is a single centred card and the one screen in the app with no shell, no sidebar, no
    /// data grid and no chart — so "the phone layout is fine" was being concluded from the page
    /// least able to demonstrate it. These are the signed-in pages that actually carry the wide
    /// content: a data grid, a KPI tile row, three charts and a heatmap.
    /// </summary>
    public static TheoryData<string> SignedInRoutes() => new() { "/", "/insights", "/settings", "/diag" };

    [SkippableTheory]
    [MemberData(nameof(SignedInRoutes))]
    public async Task Mobile_SignedInPagesDoNotScrollHorizontally(string route)
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, route);
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 25000 });
        // The grids and charts size themselves after their data arrives, so the overflow this is
        // looking for does not exist yet at first paint.
        await page.WaitForTimeoutAsync(2500);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1,
            $"{route} must fit a 390px viewport — the wide data grids contain their own scrolling via .u-scroll-x");
    }

    [SkippableTheory]
    [MemberData(nameof(SignedInRoutes))]
    public async Task Mobile_SignedInPagesMeetTheMinimumTapTargetSize(string route)
    {
        // WCAG 2.2 AA, 2.5.8. The anonymous /login version of this check passes vacuously for
        // every control that only exists behind sign-in — the grid's row actions, the chart range
        // selector and the custom chart legend among them.
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Mobile, route);
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 25000 });
        await page.WaitForTimeoutAsync(2500);

        var undersized = await page.EvaluateAsync<string[]>(@"() =>
            [...document.querySelectorAll('button, a, [role=button]')]
                .filter(el => el.offsetParent !== null)
                .filter(el => { const r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 && (r.width < 24 || r.height < 24); })
                .map(el => (el.className || el.tagName) + ' :: ' + (el.textContent || '').trim().slice(0, 20))");

        undersized.Should().BeEmpty($"every visible control on {route} must meet the 24x24 minimum target size");
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
