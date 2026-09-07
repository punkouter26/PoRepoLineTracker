using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace PoRepoLineTracker.Integration;

public class DevAuthFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.GetTempPath());
        builder.UseSetting(ConfigKeys.Security.RequireSecureCookies, "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "GitHub:ClientId", "" },
                { "GitHub:ClientSecret", "" },
                { "GitHub:Dev:ClientId", "" },
                { "GitHub:Dev:ClientSecret", "" },
                { "KeyVault:Uri", "" },
                { "OpenTelemetry:OtlpEndpoint", "" },
                { "EnableConsoleExporters", "false" },
                { "APPLICATIONINSIGHTS_CONNECTION_STRING", "" },
                { "ApplicationInsights:ConnectionString", "" },
                { "AzureTableStorage:ConnectionString", "UseDevelopmentStorage=true" },
                { "GitHub:LocalReposPath", CustomWebApplicationFactory.TestRepoRoot }
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRepositoryDataService>();
            services.RemoveAll<IUserService>();
            services.RemoveAll<IUserPreferencesService>();
            services.RemoveAll<IGitHubService>();

            var repoData = Substitute.For<IRepositoryDataService>();
            repoData.GetAllRepositoriesAsync(Arg.Any<UserId>())
                .Returns(Task.FromResult(Enumerable.Empty<GitHubRepository>()));

            services.AddScoped(_ => repoData);
            services.AddScoped(_ => Substitute.For<IUserService>());
            services.AddScoped(_ => Substitute.For<IUserPreferencesService>());
            services.AddScoped(_ => Substitute.For<IGitHubService>());
        });

        builder.UseEnvironment("Development");
    }
}

public class DevAuthEndpointTests : IClassFixture<DevAuthFactory>
{
    private readonly DevAuthFactory _factory;

    public DevAuthEndpointTests(DevAuthFactory factory) => _factory = factory;

    [Fact]
    public async Task AuthLogin_InDevelopmentWithoutProvider_SignsInAndRedirects()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/auth/login");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/");
        response.Headers.Should().ContainKey("Set-Cookie");
        var cookie = response.Headers.GetValues("Set-Cookie").First();
        cookie.Should().Contain("PoRepoLineTracker.Auth");
    }

    [Fact]
    public async Task AuthLogin_WithReturnUrl_RedirectsToTargetOrFallsBackToSlash()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var valid = await client.GetAsync("/auth/login?returnUrl=/repositories");
        valid.StatusCode.Should().Be(HttpStatusCode.Redirect);
        valid.Headers.Location?.ToString().Should().Be("/repositories");

        var invalid = await client.GetAsync("/auth/login?returnUrl=https://evil.com");
        invalid.StatusCode.Should().Be(HttpStatusCode.Redirect);
        invalid.Headers.Location?.ToString().Should().Be("/");
    }

    [Fact]
    public async Task AuthLogin_AfterDevLogin_AuthMeReturnsAuthenticated()
    {
        // Client with cookie container so it retains the session cookie
        var client = _factory.CreateClient();

        var loginResponse = await client.GetAsync("/auth/login");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var meResponse = await client.GetAsync("/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await meResponse.Content.ReadAsStringAsync();
        body.Should().Contain("\"isAuthenticated\":true");
        body.Should().Contain("\"username\":\"DevUser\"");
    }

    [Fact]
    public async Task UserRepositories_InDevelopmentWithoutToken_ReturnsDevSampleRepositories()
    {
        var client = _factory.CreateClient();

        // Sign in first
        var loginResponse = await client.GetAsync("/auth/login");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/github/user-repositories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PoRepoLineTracker");
        body.Should().Contain("punkouter26");
    }
}
