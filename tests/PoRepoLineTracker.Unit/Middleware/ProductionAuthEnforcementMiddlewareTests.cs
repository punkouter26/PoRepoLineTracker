using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;
using PoRepoLineTracker.API.Middleware;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The production sign-in gate, tested directly because nothing else can reach it.
///
/// <para>This middleware is a no-op outside Production, and every tier that drives a real host —
/// Integration ("Test"), E2EAPI and E2EUI (Development) — therefore runs straight past it. That is
/// exactly how it came to send every unauthenticated page request to github.com: local said 200,
/// the deployed site said 302 to another origin, and no test could see the difference. Unit tests
/// against a substituted environment are the only place the Production branch executes.</para>
/// </summary>
public class ProductionAuthEnforcementMiddlewareTests
{
    private bool _nextCalled;

    private ProductionAuthEnforcementMiddleware Build(string environmentName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        return new ProductionAuthEnforcementMiddleware(
            _ => { _nextCalled = true; return Task.CompletedTask; },
            env,
            Substitute.For<ILogger<ProductionAuthEnforcementMiddleware>>());
    }

    private static DefaultHttpContext Request(string path, string query = "", bool authenticated = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "test"));

        return context;
    }

    private async Task<DefaultHttpContext> InvokeAsync(string environment, string path, string query = "", bool authenticated = false)
    {
        var context = Request(path, query, authenticated);
        await Build(environment).InvokeAsync(context);
        return context;
    }

    // ─── Non-production is a no-op ───────────────────────────────────────────

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task OutsideProduction_EverythingPassesThrough(string environment)
    {
        await InvokeAsync(environment, "/insights");

        _nextCalled.Should().BeTrue("local runs and the E2E tiers work unauthenticated");
    }

    // ─── The redirect target ─────────────────────────────────────────────────

    /// <summary>
    /// The defect this file exists for. Challenging the OAuth scheme sent the browser to
    /// github.com — skipping the app's own sign-in page, and taking the installed PWA (whose
    /// start_url is "/") out of its own scope on launch.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/insights")]
    [InlineData("/recap/2025")]
    [InlineData("/repositories/abc")]
    public async Task Production_UnauthenticatedPage_RedirectsToTheAppsOwnLoginPage(string path)
    {
        var context = await InvokeAsync("Production", path);

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);

        var location = context.Response.Headers.Location.ToString();
        location.Should().StartWith("/login?returnUrl=", "sign-in starts on this origin, not at the provider");
        location.Should().NotContain("github.com", "the provider is reached by pressing the button on /login");
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Production_RedirectCarriesTheOriginalPathAndQuery()
    {
        var context = await InvokeAsync("Production", "/recap/2025", "?tab=languages");

        var location = context.Response.Headers.Location.ToString();
        Uri.UnescapeDataString(location).Should().Contain("/recap/2025?tab=languages",
            "a user who followed a deep link must land back on it after signing in");
    }

    /// <summary>
    /// The returnUrl must stay a path on this origin. An absolute URI here would make the
    /// post-sign-in redirect an open redirect to anyone else's host.
    /// </summary>
    [Fact]
    public async Task Production_ReturnUrlIsRelative_NotAnAbsoluteUri()
    {
        var context = await InvokeAsync("Production", "/insights");

        var returnUrl = Uri.UnescapeDataString(
            context.Response.Headers.Location.ToString().Split("returnUrl=")[1]);

        Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute)
            .Should().BeFalse("an absolute returnUrl is an open redirect");
        returnUrl.Should().StartWith("/");
    }

    // ─── What must never be gated ────────────────────────────────────────────

    [Fact]
    public async Task Production_TheLoginPageItself_IsNeverRedirected()
    {
        await InvokeAsync("Production", "/login");

        _nextCalled.Should().BeTrue("redirecting /login to /login is an infinite loop");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/auth/login")]
    [InlineData("/auth/logout")]
    [InlineData("/signin-github")]
    public async Task Production_PublicEndpoints_PassThrough(string path)
    {
        await InvokeAsync("Production", path);

        _nextCalled.Should().BeTrue();
    }

    /// <summary>
    /// Segment-aware matching. A raw StartsWith would let "/loginfoo" walk past the gate on the
    /// strength of sharing a prefix with "/login".
    /// </summary>
    [Theory]
    [InlineData("/loginfoo")]
    [InlineData("/healthcheck-public")]
    public async Task Production_PathsThatMerelySharePrefix_AreStillGated(string path)
    {
        var context = await InvokeAsync("Production", path);

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
    }

    /// <summary>
    /// /api is left to routing and the FallbackPolicy. Answering here — before routing — meant a
    /// real endpoint and a typo were indistinguishable, and both came back 401.
    /// </summary>
    [Theory]
    [InlineData("/api/repositories")]
    [InlineData("/api/definitely-not-a-route")]
    public async Task Production_ApiRequests_AreLeftToRouting(string path)
    {
        var context = await InvokeAsync("Production", path);

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status302Found,
            "an XHR cannot follow a cross-origin redirect — the cookie handler answers these 401");
    }

    [Fact]
    public async Task Production_HubNegotiate_IsLeftToTheHub()
    {
        await InvokeAsync("Production", "/hubs/analysis");

        _nextCalled.Should().BeTrue("the hub carries [Authorize] and rejects the connection itself");
    }

    [Fact]
    public async Task Production_AuthenticatedRequest_PassesThrough()
    {
        await InvokeAsync("Production", "/insights", authenticated: true);

        _nextCalled.Should().BeTrue();
    }
}
