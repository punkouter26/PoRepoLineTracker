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
using PoRepoLineTracker.Application.Models;
using MediatR;
using Moq;
using PoRepoLineTracker.Application.Services.LineCounters;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Infrastructure.Interfaces; // Add this using directive
using PoRepoLineTracker.Application.Models; // Add this using directive for GitHubRepoStatsDto
using Microsoft.Extensions.DependencyInjection; // Add this using directive for ServiceCollection and GetRequiredService

namespace PoRepoLineTracker.IntegrationTests;

public class RepositoryDataServiceIntegrationTests : IDisposable
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly IGitHubService _gitHubService;
    private readonly IRepositoryService _repositoryService;
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<RepositoryDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _testLocalReposPath;
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IGitClient> _mockGitClient; // Add Mock for IGitClient
    private readonly Mock<IGitHubService> _mockGitHubService; // Add Mock for IGitHubService

    private const string TestRepositoryTableName = "PoRepoLineTrackerRepositoriesTest";
    private const string TestCommitLineCountTableName = "PoRepoLineTrackerCommitLineCountsTest";
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";

    private readonly IServiceProvider _serviceProvider;

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

        // Initialize ILineCounter implementations
        var lineCounters = new List<ILineCounter>
        {
            new DefaultLineCounter(),
            new CSharpLineCounter()
        };

        // Initialize mocks
        _mockGitClient = new Mock<IGitClient>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockMediator = new Mock<IMediator>();

        // Set up a ServiceCollection for dependency injection
        var services = new ServiceCollection();

        // Register logging services first - this is required for MediatR
        services.AddLogging(builder => builder.AddConsole());

        // Register configuration
        services.AddSingleton(_configuration);

        services.AddScoped<IRepositoryDataService, RepositoryDataService>();
        services.AddScoped<IGitClient>(provider => _mockGitClient.Object); // Use mocked GitClient
        services.AddScoped<IGitHubService>(provider => _mockGitHubService.Object); // Use mocked GitHubService
        services.AddScoped<IRepositoryService, RepositoryService>();

        // Register real MediatR instead of mocked one
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand).Assembly));

        // Register line counters for the handlers
        services.AddScoped<ILineCounter, DefaultLineCounter>();
        services.AddScoped<ILineCounter, CSharpLineCounter>();

        _serviceProvider = services.BuildServiceProvider();

        // Resolve services from the service provider
        _repositoryDataService = _serviceProvider.GetRequiredService<IRepositoryDataService>();
        _gitHubService = _serviceProvider.GetRequiredService<IGitHubService>(); // This will be the mocked one
        _repositoryService = _serviceProvider.GetRequiredService<IRepositoryService>(); // This will use the mocked Mediator and DataService

        // Get logger from DI
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<RepositoryDataService>();

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

        // Mock GitClient to return a dummy commit
        _mockGitClient.Setup(g => g.Clone(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns((string url, string path) => path); // Simulate successful clone and return local path
        _mockGitClient.Setup(g => g.GetCommits(It.IsAny<string>(), It.IsAny<DateTime?>()))
                      .Returns(new List<(string Sha, DateTimeOffset CommitDate)>
                      {
                          ("dummySha1", DateTimeOffset.UtcNow)
                      });

        // Mock GitHubService methods that RepositoryService will call
        _mockGitHubService.Setup(g => g.CloneRepositoryAsync(It.IsAny<string>(), It.IsAny<string>()))
                          .ReturnsAsync((string url, string path) => path); // Simulate successful clone
        _mockGitHubService.Setup(g => g.GetCommitStatsAsync(It.IsAny<string>(), It.IsAny<DateTime?>()))
                          .ReturnsAsync(new List<CommitStatsDto>
                          {
                              new CommitStatsDto 
                              { 
                                  Sha = "dummySha1", 
                                  CommitDate = DateTime.UtcNow, 
                                  LinesAdded = 50, 
                                  LinesRemoved = 10 
                              }
                          });
        _mockGitHubService.Setup(g => g.CountLinesInCommitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                          .ReturnsAsync(new Dictionary<string, int> { { ".cs", 100 } }); // Simulate line counting

        // Act - Add the repository and get the actual repository ID
        var addedRepo = await _repositoryService.AddRepositoryAsync(owner, repoName, cloneUrl);
        var repoId = addedRepo.Id;

        // Act - Analyze commits
        await _repositoryService.AnalyzeRepositoryCommitsAsync(repoId);

        // Assert - Retrieve and verify commit line counts
        var retrievedCommits = (await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repoId)).ToList();

        Assert.NotNull(retrievedCommits);
        Assert.True(retrievedCommits.Any(), "Should have retrieved at least one commit.");
        Assert.Single(retrievedCommits); // Expecting exactly one dummy commit
        var retrievedCommit = retrievedCommits.First();
        Assert.Equal("dummySha1", retrievedCommit.CommitSha);
        Assert.True(retrievedCommit.TotalLines > 0, "Total lines of code should be greater than 0.");
        Assert.Equal(50, retrievedCommit.LinesAdded); // Check that lines added is properly set
        Assert.Equal(10, retrievedCommit.LinesRemoved); // Check that lines removed is properly set
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

}
