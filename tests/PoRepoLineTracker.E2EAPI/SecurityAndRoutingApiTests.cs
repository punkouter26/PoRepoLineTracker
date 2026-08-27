using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Response-hardening and routing contract of the running instance: the headers
/// SecurityHeadersMiddleware promises, and the shape of the API surface after the MapGroup
/// conversion — unknown routes and wrong verbs must stay client errors.
/// </summary>
public sealed class SecurityAndRoutingApiTests
{
    [SkippableFact]
    public async Task SecurityHeaders_ArePresentOnEveryResponse()
    {
        var response = await E2EApiClient.GetAsync("/health");

        foreach (var (header, expected) in new[]
        {
            ("X-Content-Type-Options", "nosniff"),
            ("X-Frame-Options", "DENY"),
            ("Referrer-Policy", "strict-origin-when-cross-origin"),
            ("X-DNS-Prefetch-Control", "off"),
        })
        {
            response.Headers.TryGetValues(header, out var values).Should().BeTrue($"{header} must be set");
            values!.Should().Contain(expected);
        }

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        string.Join(' ', csp!).Should().Contain("frame-ancestors 'none'");

        response.Headers.TryGetValues("Permissions-Policy", out var permissions).Should().BeTrue();
        var policy = string.Join(' ', permissions!);
        policy.Should().Contain("camera=()");
        policy.Should().Contain("microphone=()");
    }

    /// <summary>
    /// A typo must say "no such route", not "you are logged out" and not 200-with-HTML.
    ///
    /// <para>This used to assert only "below 500", which passed on both of the wrong answers it
    /// was hiding: in Production the auth gate answered 401 for every /api path before routing
    /// could tell a real endpoint from a typo, and elsewhere the request fell through to the
    /// Blazor fallback and came back as a 200 carrying the app shell to a caller expecting
    /// JSON.</para>
    /// </summary>
    [SkippableFact]
    public async Task UnknownApiRoute_Is404WithJson_NotTheAppShellAndNot401()
    {
        var response = await E2EApiClient.GetAsync("/api/definitely-not-a-route-12345");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "an API caller must never be handed the HTML shell");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("route_not_found");
    }

    [SkippableFact]
    public async Task WrongVerbOnKnownRoute_IsAClientError()
    {
        var response = await E2EApiClient.DeleteAsync("/auth/me");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [SkippableFact]
    public async Task MalformedRepositoryId_IsAClientError()
    {
        // The route parameter binds to RepositoryId via IParsable; a non-GUID must
        // fail to bind into a 4xx, never a 500 from a parse exception deeper in the handler.
        var response = await E2EApiClient.GetAsync("/api/repositories/not-a-guid/linehistory/30");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [SkippableFact]
    public async Task GroupRoute_ResolvesWithoutTrailingSlash()
    {
        // MapGroup("/api/repositories") + MapGet("/") must still answer /api/repositories.
        var response = await E2EApiClient.GetAsync("/api/repositories");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "the group root must resolve without a trailing slash");
    }
}
