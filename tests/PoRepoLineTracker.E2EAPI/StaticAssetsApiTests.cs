using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Regression guard for the middleware ordering that the FallbackPolicy (Rule 3.3) made
/// load-bearing: the authorization middleware applies the fallback to requests that matched no
/// endpoint, and static files are served by middleware rather than endpoints. With
/// UseStaticFiles/UseBlazorFrameworkFiles registered after UseAuthorization, every asset answered
/// 302-to-login and the browser rendered Blazor's "unhandled error" shell instead of the app.
///
/// This lives in the E2E tier because it needs a real content root — the integration test host
/// points the content root at a temp directory and has no wwwroot to serve.
/// </summary>
public sealed class StaticAssetsApiTests
{
    [SkippableTheory]
    [InlineData("/css/app.css")]
    [InlineData("/_framework/blazor.webassembly.js")]
    [InlineData("/favicon.png")]
    [InlineData("/manifest.json")]
    public async Task StaticAsset_Anonymous_IsServed(string asset)
    {
        var response = await E2EApiClient.GetAsync(asset);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an anonymous browser must be able to load the app shell's assets");
    }

    [SkippableFact]
    public async Task Stylesheet_IsServedAsCss()
    {
        var response = await E2EApiClient.GetAsync("/css/app.css");

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/css");
    }

    [SkippableFact]
    public async Task ScopedComponentStylesheet_IsServed()
    {
        // Rule 4.3 — the bundle Blazor generates from the .razor.css files. If CSS isolation is
        // not wired up this 404s and every extracted component style silently disappears.
        var response = await E2EApiClient.GetAsync("/PoRepoLineTracker.Client.styles.css");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// One representative class per extracted component. A .razor.css that fails to compile into
    /// the bundle takes its component's styling with it and produces no build error, so the
    /// bundle's contents are the only cheap proof that the extraction is wired up.
    /// </summary>
    public static TheoryData<string, string> ExtractedComponentClasses() => new()
    {
        { "login-cta-btn", "Login.razor.css" },
        { "mock-data-banner", "MockDataBanner.razor.css" },
        { "nav-section-label", "NavMenu.razor.css" },
        { "settings-title", "Settings.razor.css" },
        { "chart-display-card__title", "ChartDisplayModeCard.razor.css" },
        { "login-display__avatar", "LoginDisplay.razor.css" },
        { "add-repo-title", "AddRepository.razor.css" },
        { "top-files-dialog__grid", "TopFilesDialog.razor.css" },
        { "brand-wordmark", "MainLayout.razor.css" },
        { "ext-code", "ExtensionsCounted.razor.css" },
        { "ghsel-list", "GitHubRepositorySelector.razor.css" },
        { "mrc-metric-value", "MobileRepoCard.razor.css" },
        { "xc-env-card", "ExternalConnections.razor.css" },
        { "ul-dropzone", "UploadRepository.razor.css" },
        { "arc-legend__swatch", "AllReposComparisonChart.razor.css" },
        { "cc-bar", "ContributorChart.razor.css" },
        { "ts-metric__value", "TimelineScrubber.razor.css" },
        { "rd-ext-bar", "RepositoryDetail.razor.css" },
        { "rp-stat-icon", "Repositories.razor.css" },
    };

    [SkippableTheory]
    [MemberData(nameof(ExtractedComponentClasses))]
    public async Task ScopedComponentStylesheet_ContainsTheExtractedRules(string cssClass, string source)
    {
        var response = await E2EApiClient.GetAsync("/PoRepoLineTracker.Client.styles.css");
        var css = await response.Content.ReadAsStringAsync();

        css.Should().Contain(cssClass, $"{source} must be part of the scoped bundle");
    }

    [SkippableFact]
    public async Task ScopedComponentStylesheet_CarriesIsolationAttributes()
    {
        // Every rule in the bundle should be scoped (b-xxxxxxxxxx). A bundle with no scope
        // attributes would mean isolation silently degraded to global CSS.
        var response = await E2EApiClient.GetAsync("/PoRepoLineTracker.Client.styles.css");
        var css = await response.Content.ReadAsStringAsync();

        css.Should().MatchRegex(@"\[b-[a-z0-9]+\]");
    }

    [SkippableFact]
    public async Task AppShell_IsServedAnonymously()
    {
        var response = await E2EApiClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
