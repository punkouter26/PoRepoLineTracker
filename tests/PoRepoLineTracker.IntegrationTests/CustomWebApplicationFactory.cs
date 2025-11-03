using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Api;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Infrastructure.Interfaces;
using NSubstitute;
using System.Collections.Generic;

namespace PoRepoLineTracker.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Setup test configuration
                var inMemorySettings = new Dictionary<string, string?>
                {
                    {"AzureTableStorage:ConnectionString", "UseDevelopmentStorage=true"},
                    {"AzureTableStorage:RepositoryTableName", "PoRepoLineTrackerRepositoriesTest"},
                    {"AzureTableStorage:CommitLineCountTableName", "PoRepoLineTrackerCommitLineCountsTest"},
                    {"GitHub:LocalReposPath", System.IO.Path.GetTempPath()}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(inMemorySettings)
                    .Build();

                // Replace configuration
                services.AddSingleton<IConfiguration>(configuration);

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
