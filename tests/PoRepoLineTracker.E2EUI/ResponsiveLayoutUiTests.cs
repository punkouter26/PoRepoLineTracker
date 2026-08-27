using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// The UI tier must cover mobile and desktop. There is also a performance angle:
/// the page must not scroll horizontally on a phone, which is the usual symptom of a fixed-width
/// element left over from a desktop-only layout.
///
/// <para>Parameterised over (viewport × route) rather than one fact per page: every row runs the
/// same assertion, and the signed-in routes matter most — /login is a single centred card and the
/// one screen in the app with no shell, no sidebar, no data grid and no chart, so "the phone
/// layout is fine" must not be concluded from the page least able to demonstrate it.</para>
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class ResponsiveLayoutUiTests
{
    private readonly E2EUiFixture _fixture;

    public ResponsiveLayoutUiTests(E2EUiFixture fixture) => _fixture = fixture;

    private static ViewportSize Viewport(string name) =>
        name == "mobile" ? E2EUiFixture.Mobile : E2EUiFixture.Desktop;

    /// <summary>/login is reachable anonymously; everything else needs the fake-user header.</summary>
    private Task<IPage> OpenRouteAsync(string viewportName, string route) =>
        route == "/login"
            ? _fixture.OpenAsync(Viewport(viewportName), route)
            : _fixture.OpenAuthenticatedAsync(Viewport(viewportName), route);

    [SkippableTheory]
    [InlineData("desktop")]
    [InlineData("mobile")]
    public async Task ShellRenders(string viewportName)
    {
        var page = await _fixture.OpenAsync(Viewport(viewportName), "/login");
        await using var _ = page.Context;

        // Wait for a rendered control, not for <body>: the body element exists in index.html
        // before the WASM runtime has rendered anything, so asserting on it races the boot.
        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        (await page.InnerTextAsync("body")).Should().NotBeNullOrWhiteSpace();

        var viewportMeta = await page.GetAttributeAsync("meta[name=viewport]", "content");
        viewportMeta.Should().NotBeNullOrWhiteSpace("without it mobile browsers render at desktop width");
    }

    [SkippableTheory]
    [InlineData("mobile", "/login")]
    [InlineData("mobile", "/repositories")]
    [InlineData("mobile", "/insights")]
    [InlineData("mobile", "/settings")]
    [InlineData("mobile", "/recap")]
    public async Task NoHorizontalScroll(string viewportName, string route)
    {
        var page = await OpenRouteAsync(viewportName, route);
        await using var _ = page.Context;

        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 25000 });
        // The grids and charts size themselves after their data arrives, so the overflow this is
        // looking for does not exist yet at first paint.
        await page.WaitForTimeoutAsync(2500);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(1,
            $"{route} must fit a {Viewport(viewportName).Width}px viewport — wide data grids contain their own scrolling via .u-scroll-x");
    }

    // WCAG 2.2 AA, 2.5.8 Target Size (Minimum) — 24x24 CSS pixels. The anonymous /login version
    // of this check passes vacuously for every control that only exists behind sign-in — the
    // grid's row actions, the chart range selector and the custom chart legend among them, which
    // is why the signed-in routes are rows too.
    [SkippableTheory]
    [InlineData("/login")]
    [InlineData("/repositories")]
    [InlineData("/insights")]
    [InlineData("/recap")]
    public async Task Mobile_TapTargetsAreLargeEnough(string route)
    {
        var page = await OpenRouteAsync("mobile", route);
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
}
