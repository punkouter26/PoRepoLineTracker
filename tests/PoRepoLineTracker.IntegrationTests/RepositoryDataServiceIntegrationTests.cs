using Xunit;
using PoRepoLineTracker.Infrastructure.Services;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PoRepoLineTracker.IntegrationTests;

public class RepositoryDataServiceIntegrationTests : IDisposable
{
    private readonly RepositoryDataService _repositoryDataService;
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<RepositoryDataService> _logger;
    private readonly IConfiguration _configuration;

    private const string TestRepositoryTableName = "PoRepoLineTrackerRepositoriesTest";
    private const string TestCommitLineCountTableName = "PoRepoLineTrackerCommitLineCountsTest";
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";

    public RepositoryDataServiceIntegrationTests()
    {
        // Setup configuration for Azurite
        var inMemorySettings = new Dictionary<string, string?> {
            {"AzureTableStorage:ConnectionString", AzuriteConnectionString},
            {"AzureTableStorage:RepositoryTableName", TestRepositoryTableName},
            {"AzureTableStorage:CommitLineCountTableName", TestCommitLineCountTableName}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Setup logger
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RepositoryDataService>();

        // Initialize service
        _repositoryDataService = new RepositoryDataService(_configuration, _logger);

        // Initialize TableServiceClient for table management
        _tableServiceClient = new TableServiceClient(AzuriteConnectionString);

        // Ensure tables are clean before tests run
        // Use async methods and wait for them to complete
        _tableServiceClient.GetTableClient(TestRepositoryTableName).DeleteIfExistsAsync().Wait();
        _tableServiceClient.GetTableClient(TestCommitLineCountTableName).DeleteIfExistsAsync().Wait();

        _tableServiceClient.CreateTableIfNotExistsAsync(TestRepositoryTableName).Wait();
        _tableServiceClient.CreateTableIfNotExistsAsync(TestCommitLineCountTableName).Wait();
    }

    [Fact]
    public async Task AddAndGetRepository_ShouldWorkCorrectly()
    {
        // Arrange
        var repo = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            Owner = "integrationtest",
            Name = "testrepo1",
            CloneUrl = "https://github.com/integrationtest/testrepo1.git",
            LastAnalyzedCommitDate = DateTime.UtcNow
        };

        // Act
        await _repositoryDataService.AddRepositoryAsync(repo);
        var retrievedRepo = await _repositoryDataService.GetRepositoryByIdAsync(repo.Id);

        // Assert
        Assert.NotNull(retrievedRepo);
        Assert.Equal(repo.Id, retrievedRepo.Id);
        Assert.Equal(repo.Owner, retrievedRepo.Owner);
        Assert.Equal(repo.Name, retrievedRepo.Name);
        Assert.Equal(repo.CloneUrl, retrievedRepo.CloneUrl);
        Assert.Equal(repo.LastAnalyzedCommitDate.ToString("yyyy-MM-dd HH:mm:ss"), retrievedRepo.LastAnalyzedCommitDate.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    [Fact]
    public async Task UpdateRepository_ShouldUpdateExistingRepository()
    {
        // Arrange
        var repo = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            Owner = "integrationtest",
            Name = "testrepo2",
            CloneUrl = "https://github.com/integrationtest/testrepo2.git",
            LastAnalyzedCommitDate = DateTime.UtcNow
        };
        await _repositoryDataService.AddRepositoryAsync(repo);

        // Modify
        repo.LastAnalyzedCommitDate = DateTime.UtcNow.AddDays(1);
        repo.CloneUrl = "https://github.com/integrationtest/testrepo2-updated.git";

        // Act
        await _repositoryDataService.UpdateRepositoryAsync(repo);
        var updatedRepo = await _repositoryDataService.GetRepositoryByIdAsync(repo.Id);

        // Assert
        Assert.NotNull(updatedRepo);
        Assert.Equal(repo.LastAnalyzedCommitDate.ToString("yyyy-MM-dd HH:mm:ss"), updatedRepo.LastAnalyzedCommitDate.ToString("yyyy-MM-dd HH:mm:ss"));
        Assert.Equal(repo.CloneUrl, updatedRepo.CloneUrl);
    }

    [Fact]
    public async Task AddAndGetCommitLineCount_ShouldWorkCorrectly()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var commitLineCount = new CommitLineCount
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            CommitSha = "testsha123",
            CommitDate = DateTime.UtcNow,
            TotalLines = 100,
            LinesByFileType = new Dictionary<string, int> { { ".cs", 50 }, { ".js", 50 } }
        };

        // Act
        await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);
        var retrievedLineCounts = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repoId);

        // Assert
        Assert.Single(retrievedLineCounts);
        var retrievedCommit = retrievedLineCounts.First();
        Assert.Equal(commitLineCount.CommitSha, retrievedCommit.CommitSha);
        Assert.Equal(commitLineCount.TotalLines, retrievedCommit.TotalLines);
        Assert.Equal(commitLineCount.LinesByFileType[".cs"], retrievedCommit.LinesByFileType[".cs"]);
    }

    [Fact]
    public async Task CommitExists_ShouldReturnTrueForExistingCommit()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var commitSha = "existingcommit";
        var commitLineCount = new CommitLineCount
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            CommitSha = commitSha,
            CommitDate = DateTime.UtcNow,
            TotalLines = 10
        };
        await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);

        // Act
        var exists = await _repositoryDataService.CommitExistsAsync(repoId, commitSha);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task CommitExists_ShouldReturnFalseForNonExistingCommit()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var commitSha = "nonexistingcommit";

        // Act
        var exists = await _repositoryDataService.CommitExistsAsync(repoId, commitSha);

        // Assert
        Assert.False(exists);
    }

    public void Dispose()
    {
        // Clean up test tables after each test run
        _tableServiceClient.GetTableClient(TestRepositoryTableName).DeleteIfExistsAsync().Wait();
        _tableServiceClient.GetTableClient(TestCommitLineCountTableName).DeleteIfExistsAsync().Wait();
    }
}
