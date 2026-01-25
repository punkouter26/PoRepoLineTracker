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
                // For integration tests prefer a local Azurite instance if available (faster & more deterministic than launching containers here)
                var inMemorySettings = new Dictionary<string, string?>
                {
                    {"AzureTableStorage:RepositoryTableName", "PoRepoLineTrackerRepositoriesTest"},
                    {"AzureTableStorage:CommitLineCountTableName", "PoRepoLineTrackerCommitLineCountsTest"},
                    {"GitHub:LocalReposPath", System.IO.Path.GetTempPath()},
                    // Provide mock OAuth credentials for testing
                    {"GitHub:ClientId", "test-client-id"},
                    {"GitHub:ClientSecret", "test-client-secret"},
                    {"GitHub:CallbackPath", "/signin-github"}
                };

                // Detect local Azurite on default table port
                bool azuriteAvailable = false;
                try
                {
                    using (var tcp = new System.Net.Sockets.TcpClient())
                    {
                        var task = tcp.ConnectAsync("127.0.0.1", 10002);
                        azuriteAvailable = task.Wait(1500) && tcp.Connected;
                    }
                }
                catch { /* ignore */ }

                if (azuriteAvailable)
                {
                    var connectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM...;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
                    try
                    {
                        var serviceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
                        serviceClient.CreateTableIfNotExists("PoRepoLineTrackerRepositoriesTest");
                        serviceClient.CreateTableIfNotExists("PoRepoLineTrackerCommitLineCountsTest");

                        // Clear entities to ensure clean state
                        var repoTable = serviceClient.GetTableClient("PoRepoLineTrackerRepositoriesTest");
                        foreach (var entity in repoTable.Query<Azure.Data.Tables.TableEntity>())
                        {
                            repoTable.DeleteEntity(entity.PartitionKey, entity.RowKey);
                        }

                        var lineTable = serviceClient.GetTableClient("PoRepoLineTrackerCommitLineCountsTest");
                        foreach (var entity in lineTable.Query<Azure.Data.Tables.TableEntity>())
                        {
                            lineTable.DeleteEntity(entity.PartitionKey, entity.RowKey);
                        }

                        inMemorySettings["AzureTableStorage:ConnectionString"] = connectionString;
                        inMemorySettings["ConnectionStrings:tables"] = connectionString;
                        // Provide variations used by Aspire/aspire-table configuration
                        inMemorySettings["Aspire:Azure:Data:Tables:tables:ConnectionString"] = connectionString;
                        inMemorySettings["Aspire:Azure:Data:Tables:ConnectionString"] = connectionString;
                    }
                    catch
                    {
                        inMemorySettings["AzureTableStorage:ConnectionString"] = "UseDevelopmentStorage=true";
                        inMemorySettings["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
                    }
                }
                else
                {
                    // Fall back to development storage emulator if Azurite not available
                    inMemorySettings["AzureTableStorage:ConnectionString"] = "UseDevelopmentStorage=true";
                    inMemorySettings["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
                }

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

                // Replace Table-backed services with simple mocks so integration startup does not require a live table connection
                services.AddScoped<PoRepoLineTracker.Application.Interfaces.IUserService>(provider => Substitute.For<PoRepoLineTracker.Application.Interfaces.IUserService>());
                services.AddScoped<PoRepoLineTracker.Application.Interfaces.IFailedOperationService>(provider => Substitute.For<PoRepoLineTracker.Application.Interfaces.IFailedOperationService>());
                services.AddScoped<PoRepoLineTracker.Application.Interfaces.IUserPreferencesService>(provider => Substitute.For<PoRepoLineTracker.Application.Interfaces.IUserPreferencesService>());

                // Remove the real background service that requires Azure Tables and replace with a no-op to keep startup deterministic
                var descriptor = services.FirstOrDefault(d => d.ImplementationType?.FullName == "PoRepoLineTracker.Infrastructure.Services.FailedOperationBackgroundService");
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddHostedService<NoOpHostedService>();
            });

            // No-op hosted service for tests
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<NoOpHostedService>();
            });

            builder.UseEnvironment("Testing");
        }

        // No-op hosted service used to replace real background services during tests
        private class NoOpHostedService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        }
    }
}
