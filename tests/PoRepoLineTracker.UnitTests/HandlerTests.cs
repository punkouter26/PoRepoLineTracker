using FluentAssertions;
using NSubstitute;
using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Application.Features.Repositories.Queries;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.UnitTests;

public class GetFileExtensionPercentagesQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetFileExtensionPercentagesQueryHandler _sut;

    public GetFileExtensionPercentagesQueryHandlerTests()
    {
        _sut = new GetFileExtensionPercentagesQueryHandler(_dataService);
    }

    [Fact]
    public async Task Handle_NoCommits_ReturnsEmpty()
    {
        var repoId = Guid.NewGuid();
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId)
            .Returns(Enumerable.Empty<CommitLineCount>());

        var result = await _sut.Handle(new GetFileExtensionPercentagesQuery(repoId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SingleCommit_CalculatesCorrectPercentages()
    {
        var repoId = Guid.NewGuid();
        var commits = new List<CommitLineCount>
        {
            new()
            {
                RepositoryId = repoId,
                CommitSha = "abc",
                CommitDate = DateTime.UtcNow,
                TotalLines = 100,
                LinesByFileType = new Dictionary<string, int>
                {
                    { ".cs", 75 },
                    { ".js", 25 }
                }
            }
        };

        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetFileExtensionPercentagesQuery(repoId), CancellationToken.None)).ToList();

        result.Should().HaveCount(2);
        result[0].FileExtension.Should().Be(".cs");
        result[0].Percentage.Should().Be(75.0);
        result[0].LineCount.Should().Be(75);
        result[1].FileExtension.Should().Be(".js");
        result[1].Percentage.Should().Be(25.0);
    }

    [Fact]
    public async Task Handle_MultipleCommits_AggregatesCorrectly()
    {
        var repoId = Guid.NewGuid();
        var commits = new List<CommitLineCount>
        {
            new()
            {
                RepositoryId = repoId, CommitSha = "a1", CommitDate = DateTime.UtcNow,
                LinesByFileType = new Dictionary<string, int> { { ".cs", 50 }, { ".js", 30 } }
            },
            new()
            {
                RepositoryId = repoId, CommitSha = "b2", CommitDate = DateTime.UtcNow,
                LinesByFileType = new Dictionary<string, int> { { ".cs", 20 } }
            }
        };

        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetFileExtensionPercentagesQuery(repoId), CancellationToken.None)).ToList();

        result.Should().HaveCount(2);
        // .cs = 70/100 = 70%, .js = 30/100 = 30%
        result.First(r => r.FileExtension == ".cs").LineCount.Should().Be(70);
        result.First(r => r.FileExtension == ".js").LineCount.Should().Be(30);
        // Sorted descending by line count
        result[0].FileExtension.Should().Be(".cs");
    }

    [Fact]
    public async Task Handle_ZeroLineExtensions_AreExcluded()
    {
        var repoId = Guid.NewGuid();
        var commits = new List<CommitLineCount>
        {
            new()
            {
                RepositoryId = repoId, CommitSha = "x", CommitDate = DateTime.UtcNow,
                LinesByFileType = new Dictionary<string, int> { { ".cs", 100 }, { ".txt", 0 } }
            }
        };

        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetFileExtensionPercentagesQuery(repoId), CancellationToken.None)).ToList();

        result.Should().HaveCount(1);
        result[0].FileExtension.Should().Be(".cs");
    }
}

public class AddMultipleRepositoriesCommandHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly ILogger<AddMultipleRepositoriesCommandHandler> _logger = Substitute.For<ILogger<AddMultipleRepositoriesCommandHandler>>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly AddMultipleRepositoriesCommandHandler _sut;

    public AddMultipleRepositoriesCommandHandlerTests()
    {
        _sut = new AddMultipleRepositoriesCommandHandler(_dataService, _logger, _mediator);
    }

    [Fact]
    public async Task Handle_SkipsEmptyOwner()
    {
        var repos = new List<BulkRepositoryDto>
        {
            new() { Owner = "", RepoName = "repo1", CloneUrl = "https://github.com/x/repo1.git" },
            new() { Owner = "valid-owner", RepoName = "repo2", CloneUrl = "https://github.com/valid-owner/repo2.git" }
        };
        var userId = Guid.NewGuid();

        _dataService.GetRepositoryByOwnerAndNameAsync("valid-owner", "repo2", userId)
            .Returns((GitHubRepository?)null);

        var result = (await _sut.Handle(new AddMultipleRepositoriesCommand(repos, userId), CancellationToken.None)).ToList();

        result.Should().HaveCount(1);
        result[0].Owner.Should().Be("valid-owner");
    }

    [Fact]
    public async Task Handle_SkipsEmptyRepoName()
    {
        var repos = new List<BulkRepositoryDto>
        {
            new() { Owner = "owner", RepoName = "", CloneUrl = "https://github.com/owner/x.git" }
        };

        var result = (await _sut.Handle(new AddMultipleRepositoriesCommand(repos, Guid.NewGuid()), CancellationToken.None)).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsDuplicateRepo()
    {
        var userId = Guid.NewGuid();
        var existingRepo = new GitHubRepository { Id = Guid.NewGuid(), Owner = "owner", Name = "repo", UserId = userId };
        var repos = new List<BulkRepositoryDto>
        {
            new() { Owner = "owner", RepoName = "repo", CloneUrl = "https://github.com/owner/repo.git" }
        };

        _dataService.GetRepositoryByOwnerAndNameAsync("owner", "repo", userId).Returns(existingRepo);

        var result = (await _sut.Handle(new AddMultipleRepositoriesCommand(repos, userId), CancellationToken.None)).ToList();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(existingRepo.Id);
        // Should NOT call AddRepositoryAsync for existing repos
        await _dataService.DidNotReceive().AddRepositoryAsync(Arg.Any<GitHubRepository>());
    }

    [Fact]
    public async Task Handle_ContinuesOnSingleFailure()
    {
        var userId = Guid.NewGuid();
        var repos = new List<BulkRepositoryDto>
        {
            new() { Owner = "fail-owner", RepoName = "fail-repo", CloneUrl = "url1" },
            new() { Owner = "good-owner", RepoName = "good-repo", CloneUrl = "url2" }
        };

        _dataService.GetRepositoryByOwnerAndNameAsync("fail-owner", "fail-repo", userId)
            .Returns(Task.FromException<GitHubRepository?>(new InvalidOperationException("DB error")));
        _dataService.GetRepositoryByOwnerAndNameAsync("good-owner", "good-repo", userId)
            .Returns((GitHubRepository?)null);

        var result = (await _sut.Handle(new AddMultipleRepositoriesCommand(repos, userId), CancellationToken.None)).ToList();

        // Should still add the second repo despite first one failing
        result.Should().HaveCount(1);
        result[0].Owner.Should().Be("good-owner");
    }
}

public class GetAllRepositoriesLineCountHistoryQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetAllRepositoriesLineCountHistoryQueryHandler _sut;

    public GetAllRepositoriesLineCountHistoryQueryHandlerTests()
    {
        _sut = new GetAllRepositoriesLineCountHistoryQueryHandler(_dataService);
    }

    [Fact]
    public async Task Handle_NoRepositories_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        _dataService.GetAllRepositoriesAsync(userId).Returns(Enumerable.Empty<GitHubRepository>());

        var result = await _sut.Handle(new GetAllRepositoriesLineCountHistoryQuery(30, userId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FiltersCommitsByDateRange()
    {
        var userId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        var repos = new List<GitHubRepository> { new() { Id = repoId, Owner = "o", Name = "n", UserId = userId } };
        var commits = new List<CommitLineCount>
        {
            new() { RepositoryId = repoId, CommitSha = "recent", CommitDate = DateTime.UtcNow.AddDays(-5), LinesAdded = 10, LinesRemoved = 5 },
            new() { RepositoryId = repoId, CommitSha = "old", CommitDate = DateTime.UtcNow.AddDays(-60), LinesAdded = 100, LinesRemoved = 50 }
        };

        _dataService.GetAllRepositoriesAsync(userId).Returns(repos);
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetAllRepositoriesLineCountHistoryQuery(30, userId), CancellationToken.None)).ToList();

        result.Should().HaveCount(1);
        var repoHistory = result[0];
        repoHistory.DailyLineCounts.Should().HaveCount(1); // Only the recent commit
        repoHistory.DailyLineCounts.First().TotalLinesAdded.Should().Be(10);
    }

    [Fact]
    public async Task Handle_GroupsByDate_SumsCorrectly()
    {
        var userId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;
        var repos = new List<GitHubRepository> { new() { Id = repoId, Owner = "o", Name = "n", UserId = userId } };
        var commits = new List<CommitLineCount>
        {
            new() { RepositoryId = repoId, CommitSha = "a", CommitDate = today.AddHours(1), LinesAdded = 10, LinesRemoved = 2 },
            new() { RepositoryId = repoId, CommitSha = "b", CommitDate = today.AddHours(5), LinesAdded = 20, LinesRemoved = 3 }
        };

        _dataService.GetAllRepositoriesAsync(userId).Returns(repos);
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetAllRepositoriesLineCountHistoryQuery(30, userId), CancellationToken.None)).ToList();

        var dailyCounts = result[0].DailyLineCounts.ToList();
        dailyCounts.Should().HaveCount(1); // Both commits are on same day
        dailyCounts[0].TotalLinesAdded.Should().Be(30); // 10 + 20
        dailyCounts[0].TotalLinesDeleted.Should().Be(5); // 2 + 3
    }
}
