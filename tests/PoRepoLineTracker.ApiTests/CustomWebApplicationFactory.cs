using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Api;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Infrastructure.Interfaces;
using Moq;
using System.Collections.Generic;

namespace PoRepoLineTracker.ApiTests
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
                var mockGitClient = new Mock<IGitClient>();
                var mockGitHubService = new Mock<IGitHubService>();

                // Replace services with mocks
                services.AddScoped(provider => mockGitClient.Object);
                services.AddScoped(provider => mockGitHubService.Object);
            });

            builder.UseEnvironment("Testing");
        }
    }
}
