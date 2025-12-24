using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using PoRepoLineTracker.Api;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Infrastructure.Interfaces;
using NSubstitute;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace PoRepoLineTracker.IntegrationTests
{
    /// <summary>
    /// Test authentication handler that always authenticates requests with a test user
    /// </summary>
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string TestUserId = "00000000-0000-0000-0000-000000000001";
        public const string TestUsername = "testuser";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "github-123"),
                new Claim(ClaimTypes.Name, TestUsername),
                new Claim("UserId", TestUserId),
                new Claim("GitHubId", "github-123"),
                new Claim("Username", TestUsername),
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Configure app configuration FIRST - this must happen before services are registered
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var inMemorySettings = new Dictionary<string, string?>
                {
                    {"AzureTableStorage:ConnectionString", "UseDevelopmentStorage=true"},
                    {"AzureTableStorage:RepositoryTableName", "PoRepoLineTrackerRepositoriesTest"},
                    {"AzureTableStorage:CommitLineCountTableName", "PoRepoLineTrackerCommitLineCountsTest"},
                    {"GitHub:LocalReposPath", System.IO.Path.GetTempPath()},
                    // Provide mock OAuth credentials for testing
                    {"GitHub:ClientId", "test-client-id"},
                    {"GitHub:ClientSecret", "test-client-secret"},
                    {"GitHub:CallbackPath", "/signin-github"},
                    // Connection strings for Aspire-style configuration
                    {"ConnectionStrings:tables", "UseDevelopmentStorage=true"}
                };

                config.AddInMemoryCollection(inMemorySettings);
            });

            builder.ConfigureServices(services =>
            {
                // Add test authentication that bypasses real auth
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                // Configure authorization to use the test scheme
                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                        .RequireAuthenticatedUser()
                        .Build();
                });

                // Mock external dependencies for testing
                var mockGitClient = Substitute.For<IGitClient>();
                var mockGitHubService = Substitute.For<IGitHubService>();

                // Replace services with mocks
                services.AddScoped(provider => mockGitClient);
                services.AddScoped(provider => mockGitHubService);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
