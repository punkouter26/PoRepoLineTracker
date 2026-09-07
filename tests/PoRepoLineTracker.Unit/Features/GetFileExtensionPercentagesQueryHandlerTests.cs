using FluentAssertions;
using NSubstitute;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Unit;

public class GetFileExtensionPercentagesQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetFileExtensionPercentagesQueryHandler _sut;

    public GetFileExtensionPercentagesQueryHandlerTests()
    {
        _sut = new GetFileExtensionPercentagesQueryHandler(_dataService);
    }

    [Fact]
    public async Task Handle_CalculatesExtensionPercentages_AndHandlesEmptyCommits()
    {
        var emptyRepoId = RepositoryId.New();
        _dataService.GetCommitLineCountsByRepositoryIdAsync(emptyRepoId)
            .Returns(Enumerable.Empty<CommitLineCount>());

        var emptyResult = await _sut.Handle(new GetFileExtensionPercentagesQuery(emptyRepoId), CancellationToken.None);
        emptyResult.Should().BeEmpty();

        var repoId = RepositoryId.New();
        var commits = new List<CommitLineCount>
        {
            new()
            {
                RepositoryId = repoId, CommitSha = "a1", CommitDate = DateTime.UtcNow,
                LinesByFileType = new Dictionary<string, int> { { ".cs", 50 }, { ".js", 30 }, { ".txt", 0 } }
            },
            new()
            {
                RepositoryId = repoId, CommitSha = "b2", CommitDate = DateTime.UtcNow,
                LinesByFileType = new Dictionary<string, int> { { ".cs", 20 } }
            }
        };

        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetFileExtensionPercentagesQuery(repoId), CancellationToken.None)).ToList();

        // .cs = 70/100 = 70%, .js = 30/100 = 30%, .txt dropped for having no lines
        result.Should().HaveCount(2);
        result[0].FileExtension.Should().Be(".cs");
        result[0].LineCount.Should().Be(70);
        result[0].Percentage.Should().Be(70.0);
        result[1].FileExtension.Should().Be(".js");
        result[1].Percentage.Should().Be(30.0);
    }
}

public class AddMultipleRepositoriesCommandHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly ILogger<AddMultipleRepositoriesCommandHandler> _logger = Substitute.For<ILogger<AddMultipleRepositoriesCommandHandler>>();
    private readonly AddMultipleRepositoriesCommandHandler _sut;

    public AddMultipleRepositoriesCommandHandlerTests()
    {
        _sut = new AddMultipleRepositoriesCommandHandler(_dataService, _logger);
    }

    [Fact]
    public async Task Handle_ProcessesBatch_FilteringInvalidOrDuplicateAndContinuingOnFailures()
    {
        var userId = UserId.New();
        var existingRepo = new GitHubRepository { Id = RepositoryId.New(), Owner = "existing-owner", Name = "existing-repo", UserId = userId };

        var repos = new List<BulkRepositoryDto>
        {
            new() { Owner = "", RepoName = "repo1", CloneUrl = "https://github.com/x/repo1.git" },
            new() { Owner = "existing-owner", RepoName = "existing-repo", CloneUrl = "https://github.com/existing-owner/existing-repo.git" },
            new() { Owner = "fail-owner", RepoName = "fail-repo", CloneUrl = "url1" },
            new() { Owner = "valid-owner", RepoName = "valid-repo", CloneUrl = "https://github.com/valid-owner/valid-repo.git" }
        };

        _dataService.GetRepositoryByOwnerAndNameAsync("existing-owner", "existing-repo", userId).Returns(existingRepo);
        _dataService.GetRepositoryByOwnerAndNameAsync("fail-owner", "fail-repo", userId)
            .Returns(Task.FromException<GitHubRepository?>(new InvalidOperationException("DB error")));
        _dataService.GetRepositoryByOwnerAndNameAsync("valid-owner", "valid-repo", userId).Returns((GitHubRepository?)null);

        var result = await _sut.Handle(new AddMultipleRepositoriesCommand(repos, userId), CancellationToken.None);

        result.AlreadyTracked.Should().HaveCount(1);
        result.AlreadyTracked[0].Id.Should().Be(existingRepo.Id);
        result.Added.Should().HaveCount(1);
        result.Added[0].Owner.Should().Be("valid-owner");
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
    public async Task Handle_ReturnsAggregatedHistory_GroupingAndFilteringWindow()
    {
        var userId = UserId.New();
        var emptyResult = await _sut.Handle(new GetAllRepositoriesLineCountHistoryQuery(30, userId), CancellationToken.None);
        emptyResult.Should().BeEmpty();

        var repoId = RepositoryId.New();
        var today = DateTime.UtcNow.Date;
        var repos = new List<GitHubRepository> { new() { Id = repoId, Owner = "o", Name = "n", UserId = userId } };
        var commits = new List<CommitLineCount>
        {
            new() { RepositoryId = repoId, CommitSha = "a", CommitDate = today.AddHours(1), LinesAdded = 10, LinesRemoved = 2 },
            new() { RepositoryId = repoId, CommitSha = "b", CommitDate = today.AddHours(5), LinesAdded = 20, LinesRemoved = 3 },
            new() { RepositoryId = repoId, CommitSha = "old", CommitDate = today.AddDays(-60), LinesAdded = 100, LinesRemoved = 50 }
        };

        _dataService.GetAllRepositoriesAsync(userId).Returns(repos);
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repoId).Returns(commits);

        var result = (await _sut.Handle(new GetAllRepositoriesLineCountHistoryQuery(30, userId), CancellationToken.None)).ToList();

        result.Should().HaveCount(1);
        var dailyCounts = result[0].DailyLineCounts.ToList();
        dailyCounts.Should().HaveCount(1);
        dailyCounts[0].TotalLinesAdded.Should().Be(30);
        dailyCounts[0].TotalLinesDeleted.Should().Be(5);
    }
}
