using Xunit;
using PoRepoLineTracker.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LibGit2Sharp;
using PoRepoLineTracker.Infrastructure.FileFilters;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services.LineCounters;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Integration tests to verify that private GitHub repositories can be accessed
/// and commit data can be read using authentication.
/// </summary>
[Collection("IntegrationTests")]
public class PrivateRepositoryAccessTests : IAsyncLifetime
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubService> _logger;
    private readonly GitClient _gitClient;
    private readonly GitHubService _gitHubService;
    private readonly string _testLocalReposPath;

    // Test repository details - using PoDebateRap as the test private repo
    private const string TestOwner = "punkouter26";
    private const string TestPrivateRepoName = "PoDebateRap";
    private const string TestPrivateRepoCloneUrl = "https://github.com/punkouter26/PoDebateRap.git";
    private string _testRepoLocalPath;

    public PrivateRepositoryAccessTests()
    {
        _testLocalReposPath = Path.Combine(Path.GetTempPath(), "PoRepoLineTrackerPrivateTests", Guid.NewGuid().ToString());
        _testRepoLocalPath = string.Empty;

        // Setup configuration with GitHub PAT from appsettings
        var inMemorySettings = new Dictionary<string, string?> {
            {"GitHub:PAT", Environment.GetEnvironmentVariable("GITHUB_PAT") ?? "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"},
            {"GitHub:LocalReposPath", _testLocalReposPath}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<GitHubService>();

        _gitClient = new GitClient(_configuration);

        var fileIgnoreFilterLogger = loggerFactory.CreateLogger<FileIgnoreFilter>();
        var fileIgnoreFilter = new FileIgnoreFilter(fileIgnoreFilterLogger);
        var lineCounters = new ILineCounter[]
        {
            new DefaultLineCounter()
        };

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "PoRepoLineTracker-Test");
        if (_configuration["GitHub:PAT"] != null)
        {
            httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_configuration["GitHub:PAT"]}");
        }

        _gitHubService = new GitHubService(
            httpClient,
            _configuration,
            _logger,
            lineCounters,
            _gitClient,
            fileIgnoreFilter
        );
    }

    public Task InitializeAsync()
    {
        // Create test directory
        if (!Directory.Exists(_testLocalReposPath))
        {
            Directory.CreateDirectory(_testLocalReposPath);
        }
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Delete the test local repository directory
        await Task.Run(() =>
        {
            if (Directory.Exists(_testLocalReposPath))
            {
                try
                {
                    Directory.Delete(_testLocalReposPath, recursive: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to cleanup test directory: {ex.Message}");
                }
            }
        });
    }

    [Fact]
    public async Task ClonePrivateRepository_WithAuthentication_ShouldSucceed()
    {
        // Arrange
        var expectedLocalPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");

        // Act
        var actualLocalPath = await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, expectedLocalPath);
        _testRepoLocalPath = actualLocalPath;

        // Assert
        actualLocalPath.Should().NotBeNullOrEmpty();
        Directory.Exists(actualLocalPath).Should().BeTrue("the repository should be cloned to the local path");
        Repository.IsValid(actualLocalPath).Should().BeTrue("the cloned directory should be a valid git repository");

        using (var repo = new Repository(actualLocalPath))
        {
            repo.Network.Remotes["origin"].Url.Should().Be(TestPrivateRepoCloneUrl);
        }
    }

    [Fact]
    public async Task GetCommitsFromPrivateRepository_ShouldReturnCommitData()
    {
        // Arrange
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = localPath;

        // Act
        var commits = await _gitHubService.GetCommitsAsync(localPath);

        // Assert
        commits.Should().NotBeNull();
        commits.Should().NotBeEmpty("the private repository should have commits");
        
        var commitList = commits.ToList();
        commitList.Should().HaveCountGreaterThan(0);
        
        // Verify commit structure
        var firstCommit = commitList.First();
        firstCommit.Sha.Should().NotBeNullOrEmpty("each commit should have a SHA");
        firstCommit.CommitDate.Should().BeBefore(DateTimeOffset.UtcNow, "commit date should be in the past");
    }

    [Fact]
    public async Task GetCommitStatsFromPrivateRepository_ShouldReturnLineCountData()
    {
        // Arrange
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = localPath;

        // Act - Get commits from the last 365 days
        var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, DateTime.UtcNow.AddDays(-365));

        // Assert
        commitStats.Should().NotBeNull();
        commitStats.Should().NotBeEmpty("the repository should have commit statistics");
        
        var statsList = commitStats.ToList();
        
        // Verify commit stats structure
        var firstStat = statsList.First();
        firstStat.Sha.Should().NotBeNullOrEmpty();
        firstStat.CommitDate.Should().BeBefore(DateTime.UtcNow);
        
        // Verify line count data exists
        firstStat.Should().Match<CommitStatsDto>(s => 
            s.TotalLines >= 0 && 
            s.LinesAdded >= 0 && 
            s.LinesRemoved >= 0,
            "commit stats should contain valid line count data");
    }

    [Fact]
    public async Task CountLinesInPrivateRepositoryCommit_ShouldReturnLinesByFileType()
    {
        // Arrange
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = localPath;

        // Get the latest commit
        var commits = await _gitHubService.GetCommitsAsync(localPath);
        var latestCommit = commits.First();

        var fileExtensions = new[] { ".cs", ".js", ".ts", ".html", ".css", ".json", ".xml", ".razor" };

        // Act
        var lineCounts = await _gitHubService.CountLinesInCommitAsync(localPath, latestCommit.Sha, fileExtensions);

        // Assert
        lineCounts.Should().NotBeNull();
        lineCounts.Should().NotBeEmpty("the commit should contain trackable files");
        
        // Verify that line counts are positive
        lineCounts.Values.Should().AllSatisfy(count => 
            count.Should().BeGreaterThan(0, "each file type should have a positive line count"));
        
        // Verify that file extensions match expected patterns
        lineCounts.Keys.Should().AllSatisfy(ext => 
            fileExtensions.Should().Contain(ext, $"file extension {ext} should be in the tracked extensions"));
    }

    [Fact]
    public async Task PullPrivateRepository_WithAuthentication_ShouldSucceed()
    {
        // Arrange - First clone the repository
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = localPath;

        // Get initial commit count
        var initialCommits = await _gitHubService.GetCommitsAsync(localPath);
        var initialCommitCount = initialCommits.Count();

        // Act - Pull to ensure we can fetch updates (even if there are none)
        var pullResult = await _gitHubService.PullRepositoryAsync(localPath);

        // Assert
        pullResult.Should().NotBeNullOrEmpty();
        pullResult.Should().Be(localPath);
        Repository.IsValid(localPath).Should().BeTrue("repository should still be valid after pull");

        // Verify commits are still accessible
        var afterPullCommits = await _gitHubService.GetCommitsAsync(localPath);
        afterPullCommits.Count().Should().BeGreaterThanOrEqualTo(initialCommitCount, 
            "commit count should remain the same or increase after pull");
    }

    [Fact]
    public async Task PrivateRepositoryFullWorkflow_ShouldCompleteSuccessfully()
    {
        // This test simulates the full workflow of analyzing a private repository
        // Arrange
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        var fileExtensions = new[] { ".cs", ".js", ".ts", ".html", ".css", ".json", ".xml", ".razor" };

        // Act & Assert - Step 1: Clone
        var clonedPath = await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = clonedPath;
        clonedPath.Should().NotBeNullOrEmpty();

        // Step 2: Get commit stats (simulates analysis)
        var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, DateTime.UtcNow.AddDays(-365));
        commitStats.Should().NotBeEmpty();

        // Step 3: Count lines in each commit
        var processedCommits = 0;
        foreach (var stat in commitStats.Take(5)) // Process first 5 commits as a sample
        {
            var lineCounts = await _gitHubService.CountLinesInCommitAsync(localPath, stat.Sha, fileExtensions);
            lineCounts.Should().NotBeNull();
            
            var totalLines = lineCounts.Values.Sum();
            totalLines.Should().BeGreaterThan(0, $"commit {stat.Sha} should have some lines of code");
            
            processedCommits++;
        }

        // Assert
        processedCommits.Should().BeGreaterThan(0, "at least one commit should be processed successfully");
    }

    [Fact]
    public async Task GetTotalLinesOfCode_ForPrivateRepository_ShouldReturnValidCount()
    {
        // Arrange
        var localPath = Path.Combine(_testLocalReposPath, $"repo_{Guid.NewGuid()}");
        await _gitHubService.CloneRepositoryAsync(TestPrivateRepoCloneUrl, localPath);
        _testRepoLocalPath = localPath;

        var fileExtensions = new[] { ".cs", ".js", ".ts", ".html", ".css", ".json", ".xml", ".razor" };

        // Act
        var totalLines = await _gitHubService.GetTotalLinesOfCodeAsync(localPath, fileExtensions);

        // Assert
        totalLines.Should().BeGreaterThan(0, "the repository should contain code in tracked file types");
    }
}
