using Xunit;
using PoRepoLineTracker.Infrastructure.Services;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services;

namespace PoRepoLineTracker.IntegrationTests;

public class RepositoryDataServiceIntegrationTests : IDisposable
{
    private readonly RepositoryDataService _repositoryDataService;
    private readonly IGitHubService _gitHubService;
    private readonly IRepositoryService _repositoryService;
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<RepositoryDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _testLocalReposPath;

    private const string TestRepositoryTableName = "PoRepoLineTrackerRepositoriesTest";
    private const string TestCommitLineCountTableName = "PoRepoLineTrackerCommitLineCountsTest";
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";

    public RepositoryDataServiceIntegrationTests()
    {
        _testLocalReposPath = Path.Combine(Path.GetTempPath(), "PoRepoLineTrackerTests", Guid.NewGuid().ToString());

        // Setup configuration for Azurite and GitHub local path
        var inMemorySettings = new Dictionary<string, string?> {
            {"AzureTableStorage:ConnectionString", AzuriteConnectionString},
            {"AzureTableStorage:RepositoryTableName", TestRepositoryTableName},
            {"AzureTableStorage:CommitLineCountTableName", TestCommitLineCountTableName},
            {"GitHub:LocalReposPath", _testLocalReposPath}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Setup logger
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RepositoryDataService>();

        // Initialize services
        _repositoryDataService = new RepositoryDataService(_configuration, _logger);
        _gitHubService = new GitHubService(new HttpClientFactoryStub().CreateClient("test"), _configuration, LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<GitHubService>());
        _repositoryService = new RepositoryService(_gitHubService, _repositoryDataService, LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RepositoryService>());

        // Initialize TableServiceClient for table management
        _tableServiceClient = new TableServiceClient(AzuriteConnectionString);

        // Ensure tables are clean before tests run
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        // Attempt to delete tables. Ignore if they don't exist (404).
        try { repoTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Table does not exist, ignore */ }
        try { commitTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Table does not exist, ignore */ }

        // Create tables. Ignore if they already exist (409 Conflict).
        try { repoTableClient.CreateAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 409) { /* Table already exists, ignore */ }
        try { commitTableClient.CreateAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 409) { /* Table already exists, ignore */ }

        // Ensure test local repos directory is clean
        if (Directory.Exists(_testLocalReposPath))
        {
            Directory.Delete(_testLocalReposPath, true);
        }
        Directory.CreateDirectory(_testLocalReposPath);
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

    [Fact]
    public async Task AnalyzeRepositoryCommits_ShouldRetrieveAndStoreCommitsAndLineCounts()
    {
        // Arrange
        var owner = "libgit2";
        var repoName = "libgit2sharp";
        var cloneUrl = $"https://github.com/{owner}/{repoName}";

        // Act - Add the repository
        var addedRepo = await _repositoryService.AddRepositoryAsync(owner, repoName, cloneUrl);

        // Act - Analyze commits
        await _repositoryService.AnalyzeRepositoryCommitsAsync(addedRepo.Id);

        // Assert - Retrieve and verify commit line counts
        var retrievedCommits = (await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(addedRepo.Id)).ToList();

        Assert.NotNull(retrievedCommits);
        Assert.True(retrievedCommits.Any(), "Should have retrieved at least one commit.");
        Assert.True(retrievedCommits.Sum(c => c.TotalLines) > 0, "Total lines of code should be greater than 0.");
    }

    public void Dispose()
    {
        // Clean up test tables after each test run
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        try { repoTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Table does not exist, ignore */ }
        try { commitTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Table does not exist, ignore */ }

        // Clean up local Git repositories
        if (Directory.Exists(_testLocalReposPath))
        {
            try
            {
                Directory.Delete(_testLocalReposPath, true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete test local repos directory: {Message}", ex.Message);
                // Sometimes files might be locked, especially on Windows.
                // A retry mechanism or manual cleanup might be needed if this becomes a frequent issue.
            }
        }
    }

    // Helper class for HttpClientFactory stub
    public class HttpClientFactoryStub : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(); // Return a default HttpClient
        }
    }
}
