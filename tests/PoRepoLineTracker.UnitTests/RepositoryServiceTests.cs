using Xunit;
using Moq;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.UnitTests;

public class RepositoryServiceTests
{
    private readonly Mock<IGitHubService> _mockGitHubService;
    private readonly Mock<IRepositoryDataService> _mockRepositoryDataService;
    private readonly Mock<ILogger<RepositoryService>> _mockLogger;
    private readonly RepositoryService _repositoryService;

    public RepositoryServiceTests()
    {
        _mockGitHubService = new Mock<IGitHubService>();
        _mockRepositoryDataService = new Mock<IRepositoryDataService>();
        _mockLogger = new Mock<ILogger<RepositoryService>>();
        _repositoryService = new RepositoryService(_mockGitHubService.Object, _mockRepositoryDataService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task AddRepositoryAsync_ShouldAddRepositoryToDataService()
    {
        // Arrange
        var owner = "testowner";
        var repoName = "testrepo";
        var cloneUrl = "https://github.com/testowner/testrepo.git";

        _mockRepositoryDataService.Setup(s => s.AddRepositoryAsync(It.IsAny<GitHubRepository>()))
                                  .Returns(Task.CompletedTask);

        // Act
        var result = await _repositoryService.AddRepositoryAsync(owner, repoName, cloneUrl);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(owner, result.Owner);
        Assert.Equal(repoName, result.Name);
        Assert.Equal(cloneUrl, result.CloneUrl);
        _mockRepositoryDataService.Verify(s => s.AddRepositoryAsync(It.Is<GitHubRepository>(r => r.Owner == owner && r.Name == repoName && r.CloneUrl == cloneUrl)), Times.Once);
    }

    [Fact]
    public async Task AnalyzeRepositoryCommitsAsync_ShouldAnalyzeAndSaveCommits()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var localPath = Path.Combine("LocalRepos", "testowner", "testrepo");
        var lastAnalyzedDate = DateTime.UtcNow.AddDays(-7);
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LastAnalyzedCommitDate = lastAnalyzedDate
        };

        var commits = new List<(string Sha, DateTimeOffset CommitDate)> { ("sha1", DateTimeOffset.UtcNow.AddDays(-6)), ("sha2", DateTimeOffset.UtcNow.AddDays(-5)) };
        var lineCounts = new Dictionary<string, int> { { ".cs", 100 } };

        _mockRepositoryDataService.Setup(s => s.GetRepositoryByIdAsync(repoId))
                                  .ReturnsAsync(repo);
        _mockGitHubService.Setup(s => s.PullRepositoryAsync(It.IsAny<string>()))
                          .ReturnsAsync(localPath);
        _mockGitHubService.Setup(s => s.GetCommitsAsync(localPath, lastAnalyzedDate))
                          .ReturnsAsync(commits);
        _mockRepositoryDataService.Setup(s => s.CommitExistsAsync(repoId, It.IsAny<string>()))
                                  .ReturnsAsync(false);
        _mockGitHubService.Setup(s => s.CountLinesInCommitAsync(localPath, It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                          .ReturnsAsync(lineCounts);
        _mockRepositoryDataService.Setup(s => s.AddCommitLineCountAsync(It.IsAny<CommitLineCount>()))
                                  .Returns(Task.CompletedTask);
        _mockRepositoryDataService.Setup(s => s.UpdateRepositoryAsync(It.IsAny<GitHubRepository>()))
                                  .Returns(Task.CompletedTask);

        // Act
        await _repositoryService.AnalyzeRepositoryCommitsAsync(repoId);

        // Assert
        _mockRepositoryDataService.Verify(s => s.GetRepositoryByIdAsync(repoId), Times.Once);
        _mockGitHubService.Verify(s => s.PullRepositoryAsync(It.IsAny<string>()), Times.Once);
        _mockGitHubService.Verify(s => s.GetCommitsAsync(localPath, lastAnalyzedDate), Times.Once);
        _mockRepositoryDataService.Verify(s => s.CommitExistsAsync(repoId, "sha1"), Times.Once);
        _mockRepositoryDataService.Verify(s => s.CommitExistsAsync(repoId, "sha2"), Times.Once);
        _mockGitHubService.Verify(s => s.CountLinesInCommitAsync(localPath, "sha1", It.IsAny<IEnumerable<string>>()), Times.Once);
        _mockGitHubService.Verify(s => s.CountLinesInCommitAsync(localPath, "sha2", It.IsAny<IEnumerable<string>>()), Times.Once);
        _mockRepositoryDataService.Verify(s => s.AddCommitLineCountAsync(It.IsAny<CommitLineCount>()), Times.Exactly(2));
        _mockRepositoryDataService.Verify(s => s.UpdateRepositoryAsync(It.Is<GitHubRepository>(r => r.Id == repoId && r.LastAnalyzedCommitDate > lastAnalyzedDate)), Times.Once);
    }
}
