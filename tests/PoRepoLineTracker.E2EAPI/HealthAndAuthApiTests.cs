using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Rule 2.2 — pure API E2E: exercises the running system over HTTP, no UI involved.
/// Covers the two endpoints the deploy smoke test depends on (Rule 6.3): /health must answer 200
/// without a credential, and /auth/me must answer a well-formed "not signed in" to an anonymous
/// caller rather than a 401.
/// </summary>
public sealed class HealthAndAuthApiTests
{
    [SkippableFact]
    public async Task Health_Returns_200()
    {
        var response = await E2EApiClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Health_Reports_Healthy()
    {
        var response = await E2EApiClient.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Healthy");
    }

    [SkippableFact]
    public async Task Health_IsAnonymous_UnderFallbackPolicy()
    {
        // Rule 3.3 — the FallbackPolicy authenticates by default; /health opts out explicitly
        // because Azure's probe and the CI smoke test call it with no credential.
        var response = await E2EApiClient.GetAsync("/health");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Found);
    }

    [SkippableFact]
    public async Task AuthMe_Anonymous_Returns_200_NotAuthenticated()
    {
        var response = await E2EApiClient.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("isAuthenticated");
    }

    [SkippableFact]
    public async Task AuthMe_Anonymous_ReportsSignedOut()
    {
        var response = await E2EApiClient.GetAsync("/auth/me");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("false", "an anonymous caller has no session");
    }

    [SkippableFact]
    public async Task AuthLogin_Anonymous_ChallengesOrReportsNoProvider()
    {
        // With a provider configured this redirects to the provider; with none configured the
        // endpoint answers 503 rather than a generic 500 (see AuthEndpoints).
        var response = await E2EApiClient.GetAsync("/auth/login");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Found,
            HttpStatusCode.Redirect,
            HttpStatusCode.ServiceUnavailable);
    }

    [SkippableFact]
    public async Task AuthLogout_Anonymous_RedirectsToLogin()
    {
        var response = await E2EApiClient.GetAsync("/auth/logout");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.ToString().Should().Contain("/login");
    }

    [SkippableFact]
    public async Task AntiforgeryToken_Anonymous_Returns_200()
    {
        // Replaces the old /api/feature-flags check. That route was justified by "MainLayout reads
        // this before any session exists" — which had stopped being true — so the anonymous surface
        // it guarded is now exactly this one endpoint, which genuinely must precede a session.
        var response = await E2EApiClient.GetAsync("/api/antiforgery/token");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
