using Xunit;
using FluentAssertions;
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
using PoRepoLineTracker.Application.Models;
using MediatR;
using Moq;
using PoRepoLineTracker.Application.Services.LineCounters;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Infrastructure.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Integration Tests for RemoveAllRepositories functionality.
/// Tests the complete workflow from command to infrastructure data removal.
/// Covers happy path, edge cases, and error scenarios as per Section 6.2.
/// </summary>
public class RemoveAllRepositoriesIntegrationTests : IDisposable
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<RepositoryDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _testLocalReposPath;
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IGitClient> _mockGitClient;
    private readonly Mock<IGitHubService> _mockGitHubService;

    private const string TestRepositoryTableName = "PoRepoLineTrackerRepositoriesTestRemoveAll";
    private const string TestCommitLineCountTableName = "PoRepoLineTrackerCommitLineCountsTestRemoveAll";
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";

    public RemoveAllRepositoriesIntegrationTests()
    {
        // Setup test configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureTableStorage:ConnectionString"] = AzuriteConnectionString,
            ["AzureTableStorage:RepositoryTableName"] = TestRepositoryTableName,
            ["AzureTableStorage:CommitLineCountTableName"] = TestCommitLineCountTableName,
            ["GitHub:LocalReposPath"] = Path.Combine(Path.GetTempPath(), "PoRepoLineTrackerTestRemoveAll"),
            ["GitHub:PAT"] = "test-token"
        });
        _configuration = configBuilder.Build();
        _testLocalReposPath = _configuration["GitHub:LocalReposPath"]!;

        // Setup mocks
        _mockMediator = new Mock<IMediator>();
        _mockGitClient = new Mock<IGitClient>();
        _mockGitHubService = new Mock<IGitHubService>();

        // Set up a ServiceCollection for dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_configuration);
        services.AddScoped<IRepositoryDataService, RepositoryDataService>();
        services.AddScoped<IGitClient>(provider => _mockGitClient.Object);
        services.AddScoped<IGitHubService>(provider => _mockGitHubService.Object);
        services.AddScoped<IRepositoryService, RepositoryService>();

        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RemoveAllRepositoriesCommand).Assembly));

        // Register line counters
        services.AddScoped<ILineCounter, DefaultLineCounter>();
        services.AddScoped<ILineCounter, CSharpLineCounter>();

        _serviceProvider = services.BuildServiceProvider();
        _repositoryDataService = _serviceProvider.GetRequiredService<IRepositoryDataService>();

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<RepositoryDataService>();

        // Initialize TableServiceClient for table management
        _tableServiceClient = new TableServiceClient(AzuriteConnectionString);

        // Clean up and initialize test environment
        CleanupTestEnvironment();
        InitializeTestEnvironment();
    }

    private void CleanupTestEnvironment()
    {
        // Clean up test tables
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        try { repoTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Ignore */ }
        try { commitTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Ignore */ }

        // Clean up test local repos directory
        if (Directory.Exists(_testLocalReposPath))
        {
            try
            {
                Directory.Delete(_testLocalReposPath, true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete test directory during cleanup: {Message}", ex.Message);
            }
        }
    }

    private void InitializeTestEnvironment()
    {
        // Create test tables
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        try { repoTableClient.CreateAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 409) { /* Ignore */ }
        try { commitTableClient.CreateAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 409) { /* Ignore */ }

        // Create test local repos directory with some test content
        Directory.CreateDirectory(_testLocalReposPath);
    }

    [Fact]
    public async Task RemoveAllRepositoriesAsync_WithNoData_ShouldCompleteSuccessfully()
    {
        // Arrange
        // Tables are empty from initialization

        // Act
        await _repositoryDataService.RemoveAllRepositoriesAsync();

        // Assert
        var allRepos = await _repositoryDataService.GetAllRepositoriesAsync();
        allRepos.Should().BeEmpty("all repositories should be removed");

        // Verify no commit data exists
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        var repoCount = 0;
        await foreach (var _ in repoTableClient.QueryAsync<TableEntity>()) { repoCount++; }
        repoCount.Should().Be(0, "repository table should be empty");

        var commitCount = 0;
        await foreach (var _ in commitTableClient.QueryAsync<TableEntity>()) { commitCount++; }
        commitCount.Should().Be(0, "commit table should be empty");
    }

    [Fact]
    public async Task RemoveAllRepositoriesAsync_WithMultipleRepositoriesAndCommits_ShouldRemoveAllData()
    {
        // Arrange
        var testRepos = new List<GitHubRepository>
        {
            new() { Id = Guid.NewGuid(), Owner = "testowner1", Name = "testrepo1", CloneUrl = "https://github.com/testowner1/testrepo1.git" },
            new() { Id = Guid.NewGuid(), Owner = "testowner2", Name = "testrepo2", CloneUrl = "https://github.com/testowner2/testrepo2.git" },
            new() { Id = Guid.NewGuid(), Owner = "testowner3", Name = "testrepo3", CloneUrl = "https://github.com/testowner3/testrepo3.git" }
        };

        // Add test repositories
        foreach (var repo in testRepos)
        {
            await _repositoryDataService.AddRepositoryAsync(repo);
        }

        // Add test commit line counts
        var testCommits = new List<CommitLineCount>();
        foreach (var repo in testRepos)
        {
            for (int i = 0; i < 3; i++)
            {
                var commit = new CommitLineCount
                {
                    RepositoryId = repo.Id,
                    CommitSha = $"commit{i}_{repo.Name}",
                    CommitDate = DateTime.UtcNow.AddDays(-i),
                    TotalLines = 100 + i * 10,
                    LinesAdded = 50 + i * 5,
                    LinesRemoved = 20 + i * 2,
                    LinesByFileType = new Dictionary<string, int> { { ".cs", 80 }, { ".js", 20 } }
                };
                testCommits.Add(commit);
                await _repositoryDataService.AddCommitLineCountAsync(commit);
            }
        }

        // Create test local repository directories
        foreach (var repo in testRepos)
        {
            var repoPath = Path.Combine(_testLocalReposPath, $"{repo.Owner}_{repo.Name}");
            Directory.CreateDirectory(repoPath);
            File.WriteAllText(Path.Combine(repoPath, "test.txt"), "test content");
        }

        // Verify data exists before removal
        var reposBeforeRemoval = await _repositoryDataService.GetAllRepositoriesAsync();
        reposBeforeRemoval.Should().HaveCount(3, "three repositories should exist before removal");

        // Act
        await _repositoryDataService.RemoveAllRepositoriesAsync();

        // Assert
        var allRepos = await _repositoryDataService.GetAllRepositoriesAsync();
        allRepos.Should().BeEmpty("all repositories should be removed");

        // Verify all commit data is removed
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        var repoCount = 0;
        await foreach (var _ in repoTableClient.QueryAsync<TableEntity>()) { repoCount++; }
        repoCount.Should().Be(0, "repository table should be empty");

        var commitCount = 0;
        await foreach (var _ in commitTableClient.QueryAsync<TableEntity>()) { commitCount++; }
        commitCount.Should().Be(0, "commit table should be empty");
    }

    [Fact]
    public async Task RemoveAllRepositoriesCommand_ThroughMediatR_ShouldRemoveAllDataAndLocalFiles()
    {
        // Arrange
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        var testRepo = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git"
        };

        await _repositoryDataService.AddRepositoryAsync(testRepo);

        var commit = new CommitLineCount
        {
            RepositoryId = testRepo.Id,
            CommitSha = "testcommit123",
            CommitDate = DateTime.UtcNow,
            TotalLines = 150,
            LinesAdded = 100,
            LinesRemoved = 50,
            LinesByFileType = new Dictionary<string, int> { { ".cs", 120 }, { ".js", 30 } }
        };
        await _repositoryDataService.AddCommitLineCountAsync(commit);

        // Create test local repository directory
        var repoPath = Path.Combine(_testLocalReposPath, $"{testRepo.Owner}_{testRepo.Name}");
        Directory.CreateDirectory(repoPath);
        File.WriteAllText(Path.Combine(repoPath, "Program.cs"), "// Test file content");
        File.WriteAllText(Path.Combine(repoPath, "README.md"), "# Test Repository");

        // Verify files exist
        Directory.Exists(repoPath).Should().BeTrue("test repository directory should exist");
        File.Exists(Path.Combine(repoPath, "Program.cs")).Should().BeTrue("test file should exist");

        // Act
        await mediator.Send(new RemoveAllRepositoriesCommand());

        // Assert - Verify table storage is empty
        var allRepos = await _repositoryDataService.GetAllRepositoriesAsync();
        allRepos.Should().BeEmpty("all repositories should be removed");

        // Verify commit data is removed
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);
        var commitCount = 0;
        await foreach (var _ in commitTableClient.QueryAsync<TableEntity>()) { commitCount++; }
        commitCount.Should().Be(0, "commit table should be empty");

        // Verify local files are removed
        Directory.Exists(_testLocalReposPath).Should().BeFalse("local repositories directory should be removed");
    }

    [Fact]
    public async Task RemoveAllRepositoriesAsync_WithLargeDataset_ShouldHandleBatchDeletion()
    {
        // Arrange - Create many repositories and commits to test batch processing
        var testRepos = new List<GitHubRepository>();
        for (int i = 0; i < 25; i++) // Create 25 repositories
        {
            var repo = new GitHubRepository
            {
                Id = Guid.NewGuid(),
                Owner = $"owner{i}",
                Name = $"repo{i}",
                CloneUrl = $"https://github.com/owner{i}/repo{i}.git"
            };
            testRepos.Add(repo);
            await _repositoryDataService.AddRepositoryAsync(repo);

            // Add multiple commits per repository
            for (int j = 0; j < 5; j++)
            {
                var commit = new CommitLineCount
                {
                    RepositoryId = repo.Id,
                    CommitSha = $"commit{j}_repo{i}",
                    CommitDate = DateTime.UtcNow.AddDays(-j),
                    TotalLines = 100 + j * 10,
                    LinesAdded = 50 + j * 5,
                    LinesRemoved = 20 + j * 2,
                    LinesByFileType = new Dictionary<string, int> { { ".cs", 80 }, { ".js", 20 } }
                };
                await _repositoryDataService.AddCommitLineCountAsync(commit);
            }
        }

        // Verify large dataset exists
        var reposBeforeRemoval = await _repositoryDataService.GetAllRepositoriesAsync();
        reposBeforeRemoval.Should().HaveCount(25, "25 repositories should exist before removal");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _repositoryDataService.RemoveAllRepositoriesAsync();
        stopwatch.Stop();

        // Assert
        var allRepos = await _repositoryDataService.GetAllRepositoriesAsync();
        allRepos.Should().BeEmpty("all repositories should be removed");

        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);
        var commitCount = 0;
        await foreach (var _ in commitTableClient.QueryAsync<TableEntity>()) { commitCount++; }
        commitCount.Should().Be(0, "all commits should be removed");

        // Performance assertion - should complete in reasonable time
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "removal should complete within 30 seconds");
    }

    [Fact]
    public async Task RemoveAllRepositoriesAsync_WhenTablesDoNotExist_ShouldHandleGracefully()
    {
        // Arrange - Delete tables to simulate non-existent state
        var repoTableClient = _tableServiceClient.GetTableClient(TestRepositoryTableName);
        var commitTableClient = _tableServiceClient.GetTableClient(TestCommitLineCountTableName);

        try { await repoTableClient.DeleteAsync(); } catch { /* Ignore */ }
        try { await commitTableClient.DeleteAsync(); } catch { /* Ignore */ }

        // Act & Assert - Should not throw exception
        var act = async () => await _repositoryDataService.RemoveAllRepositoriesAsync();
        await act.Should().NotThrowAsync("removal should handle non-existent tables gracefully");
    }

    public void Dispose()
    {
        CleanupTestEnvironment();
        if (_serviceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }
    }
}
