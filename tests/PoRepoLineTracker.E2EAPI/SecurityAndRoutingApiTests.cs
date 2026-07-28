using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Response-hardening and routing contract of the running instance: the headers
/// SecurityHeadersMiddleware promises, and the shape of the API surface after the MapGroup
/// conversion (Rule 3.1) — unknown routes and wrong verbs must stay client errors.
/// </summary>
public sealed class SecurityAndRoutingApiTests
{
    [SkippableTheory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("X-DNS-Prefetch-Control", "off")]
    public async Task SecurityHeader_IsPresentOnEveryResponse(string header, string expected)
    {
        var response = await E2EApiClient.GetAsync("/health");

        response.Headers.TryGetValues(header, out var values).Should().BeTrue($"{header} must be set");
        values!.Should().Contain(expected);
    }

    [SkippableFact]
    public async Task ContentSecurityPolicy_BlocksFraming()
    {
        var response = await E2EApiClient.GetAsync("/health");

        response.Headers.TryGetValues("Content-Security-Policy", out var values).Should().BeTrue();
        string.Join(' ', values!).Should().Contain("frame-ancestors 'none'");
    }

    [SkippableFact]
    public async Task PermissionsPolicy_DisablesSensitiveDeviceApis()
    {
        var response = await E2EApiClient.GetAsync("/health");

        response.Headers.TryGetValues("Permissions-Policy", out var values).Should().BeTrue();
        var policy = string.Join(' ', values!);
        policy.Should().Contain("camera=()");
        policy.Should().Contain("microphone=()");
    }

    [SkippableFact]
    public async Task UnknownApiRoute_IsAClientError()
    {
        var response = await E2EApiClient.GetAsync("/api/definitely-not-a-route-12345");

        ((int)response.StatusCode).Should().BeLessThan(500,
            "an unknown route must not surface as a server error");
    }

    [SkippableFact]
    public async Task WrongVerbOnKnownRoute_IsAClientError()
    {
        var response = await E2EApiClient.DeleteAsync("/api/feature-flags");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    [SkippableFact]
    public async Task MalformedRepositoryId_IsAClientError()
    {
        // The route parameter binds to RepositoryId via IParsable (Rule 1.5); a non-GUID must
        // fail to bind into a 4xx, never a 500 from a parse exception deeper in the handler.
        var response = await E2EApiClient.GetAsync("/api/repositories/not-a-guid/top-files");

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

    [SkippableFact]
    public async Task JsonEndpoint_ReturnsJsonContentType()
    {
        var response = await E2EApiClient.GetAsync("/api/feature-flags");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
