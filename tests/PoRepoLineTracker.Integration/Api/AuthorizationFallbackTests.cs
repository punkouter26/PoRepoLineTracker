using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// Rule 3.3 — proves the server-side FallbackPolicy denies by default: an unauthenticated caller
/// is refused everywhere except the routes that explicitly opt out with AllowAnonymous.
///
/// Shares the collection's single host and signs out per request via
/// <see cref="TestAuthHandler.AnonymousHeader"/>. A second WebApplicationFactory would race the
/// entry-point resolver ("Server hasn't been initialized yet") — the same class of failure as
/// regression R2.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AuthorizationFallbackTests
{
    private readonly HttpClient _anonymous;

    public AuthorizationFallbackTests(CustomWebApplicationFactory factory)
    {
        _anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _anonymous.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
    }

    // ─── Denied without a session ───────────────────────────────────────

    [Theory]
    [InlineData("/api/repositories")]
    [InlineData("/api/repositories/allcharts/30")]
    [InlineData("/api/settings/user-preferences")]
    [InlineData("/api/github/user-repositories")]
    [InlineData("/api/diagnostics")]
    public async Task ProtectedEndpoint_Anonymous_IsDenied(string route)
    {
        var response = await _anonymous.GetAsync(route);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.Found);
    }

    [Fact]
    public async Task UnknownApiRoute_Anonymous_DoesNotLeakServerError()
    {
        var response = await _anonymous.GetAsync("/api/definitely-not-a-route");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    // ─── Explicitly public ──────────────────────────────────────────────

    [Fact]
    public async Task Health_Anonymous_Returns_200()
    {
        // The deploy smoke test polls this with no credential — it must stay anonymous.
        var response = await _anonymous.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthMe_Anonymous_Returns_200_NotAuthenticated()
    {
        var response = await _anonymous.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("false");
    }

    [Fact]
    public async Task AntiforgeryToken_Anonymous_Returns_200()
    {
        // Succeeds the old /api/feature-flags case. That route claimed MainLayout read it before
        // a session existed; nothing did, so it went, and the token endpoint is now the only
        // anonymous /api surface — it has to precede a session or no write could ever be made.
        var response = await _anonymous.GetAsync("/api/antiforgery/token");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlazorFrameworkFile_Anonymous_IsNotGatedByAuth()
    {
        // Regression guard. The FallbackPolicy is applied by the authorization middleware to
        // requests that matched NO endpoint, and static files are middleware-served, not
        // endpoints — so with UseStaticFiles/UseBlazorFrameworkFiles placed after
        // UseAuthorization every asset answered 302-to-login and the browser rendered the Blazor
        // "unhandled error" shell instead of the app.
        //
        // Only the _framework path is asserted here: it is served out of the referenced Client
        // project's build output, so it resolves under the test host too. wwwroot assets like
        // /css/app.css cannot be checked at this tier because the factory points the content root
        // at a temp directory — StaticAssetsApiTests covers those against a real instance.
        var response = await _anonymous.GetAsync("/_framework/blazor.webassembly.js");

        response.StatusCode.Should().NotBe(HttpStatusCode.Found);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BlazorShell_Anonymous_IsNotGatedByAuth()
    {
        // Gating the fallback file would make the login page itself unreachable. Under the test
        // host the content root is a temp directory with no index.html, so the interesting
        // assertion is the absence of an auth rejection — 404 means routing reached the fallback
        // and the file simply is not there; 401/302-to-login would mean the shell got gated.
        var response = await _anonymous.GetAsync("/login");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Found);
    }
}
