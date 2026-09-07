using FluentAssertions;
using Microsoft.Playwright;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// The recap page and the installability plumbing, asserted in a real browser.
///
/// <para>Both are things that look correct everywhere except in the DOM. The recap's cards are
/// revealed from C# by toggling a class, so a mistake there leaves a page that renders perfectly
/// in markup and shows nothing on screen — <c>visibility: hidden</c> is invisible to a
/// <c>textContent</c> assertion, which is why these check computed visibility. And a service
/// worker that fails to register produces no error anywhere a build or a unit test can see it; the
/// only symptom is that the install button never appears.</para>
/// </summary>
[Collection(E2EUiCollection.Name)]
public sealed class RecapAndInstallUiTests
{
    private readonly E2EUiFixture _fixture;

    public RecapAndInstallUiTests(E2EUiFixture fixture) => _fixture = fixture;

    /// <summary>Waits for the recap to settle into one of its three terminal states.</summary>
    private static async Task WaitForRecapAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".rc-card, .rc-empty, .rz-alert",
            new PageWaitForSelectorOptions { Timeout = 25000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 25000 });
    }

    [SkippableFact]
    public async Task Recap_Renders_WithoutError_ForAnAccountWithNoHistory()
    {
        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/recap");
        await using var _ = page.Context;
        await WaitForRecapAsync(page);

        // An account with nothing in it must reach the empty state, not the error path — an empty
        // year is a fact, not a failure.
        var body = await page.InnerTextAsync("body");
        body.Should().NotContain("Could not build your recap");
        body.Should().Contain("in Code");
    }

    /// <summary>
    /// The reveal is the page. Cards start hidden and are shown one at a time from C#; if that
    /// stalls, every figure the user came for is present in the DOM and invisible on screen.
    /// </summary>
    [SkippableFact]
    public async Task Recap_WithSeededHistory_RevealsItsCards()
    {
        Skip.IfNot(await E2ESeeder.EnsureSeededAsync(),
            $"Could not seed history at {E2EUiFixture.BaseUrl} — is the app running in Development?");

        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, "/recap");
        await using var _ = page.Context;
        await WaitForRecapAsync(page);

        // The seeder writes history across the last few months, so the current year has commits.
        await page.WaitForSelectorAsync(".rc-card--hero",
            new PageWaitForSelectorOptions { Timeout = 25000 });

        // Every card revealed, not merely the first. The walk steps once per card with a delay
        // between, so a stalled walk shows one card and stops.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.rc-slot--in').length >= 2",
            null,
            new PageWaitForFunctionOptions { Timeout = 25000 });

        // Computed visibility, not presence: an unrevealed card is in the DOM with
        // `visibility: hidden`, deliberately, so that revealing it does not reflow the page.
        var heroVisible = await page.EvaluateAsync<bool>(@"() => {
            const slot = document.querySelector('.rc-card--hero').closest('.rc-slot');
            return getComputedStyle(slot).visibility === 'visible';
        }");

        heroVisible.Should().BeTrue("a card that never gets its reveal class stays invisible");

        // The headline figure is the point of the card; AnimatedNumber tweens it, so it must have
        // landed on something other than its starting blank.
        var heroValue = await page.InnerTextAsync(".rc-hero-value");
        heroValue.Trim().Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task Recap_YearPicker_NavigatesToAnExplicitYear()
    {
        Skip.IfNot(await E2ESeeder.EnsureSeededAsync(),
            $"Could not seed history at {E2EUiFixture.BaseUrl} — is the app running in Development?");

        var page = await _fixture.OpenAuthenticatedAsync(E2EUiFixture.Desktop, $"/recap/{DateTime.UtcNow.Year}");
        await using var _ = page.Context;
        await WaitForRecapAsync(page);

        // The breadcrumb proves the route matched the {year:int} overload rather than falling
        // through to the year-less page.
        (await page.InnerTextAsync(".app-breadcrumb")).Should().Contain("Year in Code");
    }

    // ─── Installability ─────────────────────────────────────────────────────

    /// <summary>
    /// The three things a browser needs before it will ever consider offering an install: a linked
    /// manifest it can parse, a registered service worker, and an icon of at least 192px. None of
    /// them fails loudly — a missing one just means the prompt silently never fires.
    /// </summary>
    [SkippableFact]
    public async Task AppShell_IsInstallable_ManifestLinkedAndWorkerRegistered()
    {
        var page = await _fixture.OpenAsync(E2EUiFixture.Desktop, "/login");
        await using var _ = page.Context;
        await page.WaitForSelectorAsync("button, a", new PageWaitForSelectorOptions { Timeout = 20000 });

        var manifestHref = await page.GetAttributeAsync("link[rel=manifest]", "href");
        manifestHref.Should().NotBeNullOrWhiteSpace("without a linked manifest the app is never installable");

        var manifest = await page.EvaluateAsync<string>(
            "async href => (await fetch(href)).text()", manifestHref);

        manifest.Should().Contain("\"standalone\"", "an installed window must not be a browser tab");
        manifest.Should().Contain("192x192", "browsers require an icon of at least 192px to install");

        var registered = await page.WaitForFunctionAsync(
            "async () => !!(await navigator.serviceWorker.getRegistration())",
            null,
            new PageWaitForFunctionOptions { Timeout = 20000 });

        registered.Should().NotBeNull();

        var cacheKeys = await page.EvaluateAsync<string[]>("async () => await caches.keys()");
        cacheKeys.Should().NotContain(key => key.StartsWith("porepolinetracker-cache-"),
            "the development worker is a no-op — a populated cache here means the published one is being served");
    }
}
