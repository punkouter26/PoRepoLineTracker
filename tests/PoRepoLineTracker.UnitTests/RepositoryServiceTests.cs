using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services;
using PoRepoLineTracker.Domain.Models;
using Xunit;

namespace PoRepoLineTracker.UnitTests;

public class RepositoryServiceTests
{
    private readonly IMediator _mockMediator;
    private readonly IRepositoryDataService _mockRepositoryDataService;
    private readonly ILogger<RepositoryService> _mockLogger;
    private readonly RepositoryService _repositoryService;

    public RepositoryServiceTests()
    {
        _mockMediator = Substitute.For<IMediator>();
        _mockRepositoryDataService = Substitute.For<IRepositoryDataService>();
        _mockLogger = Substitute.For<ILogger<RepositoryService>>();
        _repositoryService = new RepositoryService(_mockMediator, _mockRepositoryDataService, _mockLogger);
    }

    [Fact]
    public async Task AddRepositoryAsync_ShouldAddRepositoryToDataService()
    {
        // Arrange
        var owner = "testowner";
        var repoName = "testrepo";
        var cloneUrl = "https://github.com/testowner/testrepo.git";
        var expectedRepo = new GitHubRepository 
        { 
            Owner = owner, 
            Name = repoName, 
            CloneUrl = cloneUrl 
        };

        _mockMediator.Send(Arg.Any<AddRepositoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(expectedRepo);

        // Act
        var result = await _repositoryService.AddRepositoryAsync(owner, repoName, cloneUrl);

        // Assert
        result.Should().NotBeNull();
        result.Owner.Should().Be(owner);
        result.Name.Should().Be(repoName);
        result.CloneUrl.Should().Be(cloneUrl);
        
        await _mockMediator.Received(1).Send(
            Arg.Is<AddRepositoryCommand>(cmd => 
                cmd.Owner == owner && 
                cmd.RepoName == repoName && 
                cmd.CloneUrl == cloneUrl), 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeRepositoryCommitsAsync_ShouldAnalyzeAndSaveCommits()
    {
        // Arrange
        var repoId = Guid.NewGuid();
        var lastAnalyzedDate = DateTime.UtcNow.AddDays(-7);
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LastAnalyzedCommitDate = lastAnalyzedDate
        };

        // Act
        await _repositoryService.AnalyzeRepositoryCommitsAsync(repoId);

        // Assert
        await _mockMediator.Received(1).Send(
            Arg.Is<AnalyzeRepositoryCommitsCommand>(cmd => cmd.RepositoryId == repoId), 
            Arg.Any<CancellationToken>());
    }
}

