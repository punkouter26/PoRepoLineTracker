using Xunit;
using Moq;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MediatR;
using PoRepoLineTracker.Application.Features.Repositories.Commands;

namespace PoRepoLineTracker.UnitTests;

public class RepositoryServiceTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IRepositoryDataService> _mockRepositoryDataService;
    private readonly Mock<ILogger<RepositoryService>> _mockLogger;
    private readonly RepositoryService _repositoryService;

    public RepositoryServiceTests()
    {
        _mockMediator = new Mock<IMediator>();
        _mockRepositoryDataService = new Mock<IRepositoryDataService>();
        _mockLogger = new Mock<ILogger<RepositoryService>>();
        _repositoryService = new RepositoryService(_mockMediator.Object, _mockRepositoryDataService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task AddRepositoryAsync_ShouldAddRepositoryToDataService()
    {
        // Arrange
        var owner = "testowner";
        var repoName = "testrepo";
        var cloneUrl = "https://github.com/testowner/testrepo.git";

        _mockMediator.Setup(m => m.Send(It.IsAny<AddRepositoryCommand>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new GitHubRepository { Owner = owner, Name = repoName, CloneUrl = cloneUrl });

        // Act
        var result = await _repositoryService.AddRepositoryAsync(owner, repoName, cloneUrl);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(owner, result.Owner);
        Assert.Equal(repoName, result.Name);
        Assert.Equal(cloneUrl, result.CloneUrl);
        _mockMediator.Verify(m => m.Send(It.Is<AddRepositoryCommand>(cmd => cmd.Owner == owner && cmd.RepoName == repoName && cmd.CloneUrl == cloneUrl), It.IsAny<CancellationToken>()), Times.Once);
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


        // Act
        await _repositoryService.AnalyzeRepositoryCommitsAsync(repoId);

        // Assert
        _mockMediator.Verify(m => m.Send(It.Is<AnalyzeRepositoryCommitsCommand>(cmd => cmd.RepositoryId == repoId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
