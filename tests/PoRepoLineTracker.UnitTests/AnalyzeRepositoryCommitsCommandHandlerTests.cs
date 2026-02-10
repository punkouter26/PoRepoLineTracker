using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.UnitTests;

public class AnalyzeRepositoryCommitsCommandHandlerTests
{
    private readonly IGitHubService _gitHubService = Substitute.For<IGitHubService>();
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly IFailedOperationService _failedOpService = Substitute.For<IFailedOperationService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserPreferencesService _prefsService = Substitute.For<IUserPreferencesService>();
    private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger = Substitute.For<ILogger<AnalyzeRepositoryCommitsCommandHandler>>();
    private readonly AnalyzeRepositoryCommitsCommandHandler _sut;

    public AnalyzeRepositoryCommitsCommandHandlerTests()
    {
        _sut = new AnalyzeRepositoryCommitsCommandHandler(
            _gitHubService, _dataService, _failedOpService,
            _userService, _prefsService, _logger);
    }

    [Fact]
    public async Task Handle_RepoNotFound_ReturnsUnitWithoutProcessing()
    {
        var repoId = Guid.NewGuid();
        _dataService.GetRepositoryByIdAsync(repoId).Returns((GitHubRepository?)null);

        var result = await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        result.Should().Be(Unit.Value);
        await _gitHubService.DidNotReceive().CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _gitHubService.DidNotReceive().PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_NewRepo_ClonesRepository()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "testowner", Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LocalPath = "" // Empty — triggers clone
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("cloned-path");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _gitHubService.Received(1).CloneRepositoryAsync(repo.CloneUrl, $"repo_{repoId}", Arg.Any<string?>());
        await _gitHubService.DidNotReceive().PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_ExistingLocalPath_PullsRepository()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "testowner", Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LocalPath = "/existing/path" // Has local path — triggers pull
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>())
            .Returns("pulled");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _gitHubService.Received(1).PullRepositoryAsync("/existing/path", Arg.Any<string?>());
        await _gitHubService.DidNotReceive().CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_ClearExistingData_DeletesCommitsAndResetsDate()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url",
            LocalPath = "/path",
            LastAnalyzedCommitDate = DateTime.UtcNow
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(
            new AnalyzeRepositoryCommitsCommand(repoId, ClearExistingData: true),
            CancellationToken.None);

        await _dataService.Received(1).DeleteCommitLineCountsForRepositoryAsync(repoId);
        // Should update repo with null LastAnalyzedCommitDate
        await _dataService.Received().UpdateRepositoryAsync(Arg.Is<GitHubRepository>(r => r.LastAnalyzedCommitDate == null));
    }

    [Fact]
    public async Task Handle_NewCommit_ProcessesAndSaves()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "abc123", CommitDate = DateTime.UtcNow, LinesAdded = 50, LinesRemoved = 10 }
        };
        var lineCounts = new Dictionary<string, int> { { ".cs", 100 }, { ".js", 50 } };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        _dataService.CommitExistsAsync(repoId, "abc123").Returns(false);
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "abc123", Arg.Any<IEnumerable<string>>())
            .Returns(lineCounts);
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _dataService.Received(1).AddCommitLineCountAsync(Arg.Is<CommitLineCount>(c =>
            c.CommitSha == "abc123" &&
            c.TotalLines == 150 &&
            c.LinesAdded == 50 &&
            c.LinesRemoved == 10 &&
            c.RepositoryId == repoId));
    }

    [Fact]
    public async Task Handle_ExistingCommit_SkipsWithoutForce()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "existing-sha", CommitDate = DateTime.UtcNow, LinesAdded = 10, LinesRemoved = 5 }
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        _dataService.CommitExistsAsync(repoId, "existing-sha").Returns(true); // Already processed
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        // Should NOT count lines since commit already exists and ForceReanalysis=false
        await _gitHubService.DidNotReceive().CountLinesInCommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
        await _dataService.DidNotReceive().AddCommitLineCountAsync(Arg.Any<CommitLineCount>());
    }

    [Fact]
    public async Task Handle_CommitProcessingFails_RecordsFailedOperation()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "fail-sha", CommitDate = DateTime.UtcNow, LinesAdded = 5, LinesRemoved = 2 }
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        _dataService.CommitExistsAsync(repoId, "fail-sha").Returns(false);
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "fail-sha", Arg.Any<IEnumerable<string>>())
            .ThrowsAsync(new InvalidOperationException("Git error"));
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        // Should NOT throw — errors are caught and recorded
        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _failedOpService.Received(1).RecordFailedOperationAsync(Arg.Is<FailedOperation>(f =>
            f.RepositoryId == repoId &&
            f.OperationType == "CommitProcessing" &&
            f.EntityId == "fail-sha" &&
            f.ErrorMessage == "Git error"));
    }

    [Fact]
    public async Task Handle_ContinuesProcessingAfterCommitFailure()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "fail-sha", CommitDate = DateTime.UtcNow, LinesAdded = 0, LinesRemoved = 0 },
            new() { Sha = "good-sha", CommitDate = DateTime.UtcNow, LinesAdded = 10, LinesRemoved = 5 }
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        _dataService.CommitExistsAsync(repoId, Arg.Any<string>()).Returns(false);

        // First commit fails, second succeeds
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "fail-sha", Arg.Any<IEnumerable<string>>())
            .ThrowsAsync(new Exception("boom"));
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "good-sha", Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, int> { { ".cs", 10 } });
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        // The good commit should still be saved
        await _dataService.Received(1).AddCommitLineCountAsync(Arg.Is<CommitLineCount>(c => c.CommitSha == "good-sha"));
    }

    [Fact]
    public async Task Handle_UserWithId_FetchesAccessToken()
    {
        var repoId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url",
            LocalPath = "/path", UserId = userId
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _userService.GetAccessTokenAsync(userId).Returns("ghp_test_token");
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<TopFileDto>());
        _prefsService.GetFileExtensionsAsync(userId).Returns(new List<string> { ".cs", ".ts" });

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _userService.Received(1).GetAccessTokenAsync(userId);
        await _gitHubService.Received(1).PullRepositoryAsync("/path", "ghp_test_token");
        await _prefsService.Received(1).GetFileExtensionsAsync(userId);
    }

    [Fact]
    public async Task Handle_TopFilesCalculationFails_DoesNotThrow()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = "/path"
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _gitHubService.GetTopFilesByLineCountAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException("top files error"));

        // Should NOT throw — top files errors are caught
        var act = () => _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_CloneError_Throws()
    {
        var repoId = Guid.NewGuid();
        var repo = new GitHubRepository
        {
            Id = repoId, Owner = "o", Name = "n", CloneUrl = "url", LocalPath = ""
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .ThrowsAsync(new InvalidOperationException("clone failed"));

        var act = () => _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("clone failed");
    }
}
