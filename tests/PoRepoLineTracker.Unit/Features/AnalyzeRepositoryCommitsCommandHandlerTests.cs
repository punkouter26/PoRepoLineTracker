using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace PoRepoLineTracker.Unit;

public class AnalyzeRepositoryCommitsCommandHandlerTests
{
    private readonly IGitHubService _gitHubService = Substitute.For<IGitHubService>();
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserPreferencesService _prefsService = Substitute.For<IUserPreferencesService>();
    private readonly IAnalysisProgressService _progressService = Substitute.For<IAnalysisProgressService>();
    private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
    private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger = Substitute.For<ILogger<AnalyzeRepositoryCommitsCommandHandler>>();
    private readonly AnalyzeRepositoryCommitsCommandHandler _sut;

    public AnalyzeRepositoryCommitsCommandHandlerTests()
    {
        _sut = new AnalyzeRepositoryCommitsCommandHandler(
            _gitHubService, _dataService,
            _userService, _prefsService, _progressService,
            _configuration, _logger);
    }

    /// <summary>
    /// What storage already holds for the repository.
    ///
    /// <para>The handler loads this ONCE and answers "have I seen this SHA" from it, rather than
    /// asking storage per commit — so an already-analysed commit is expressed here, by being in
    /// the set, and not by stubbing a per-SHA existence check. Stubbing that check was what these
    /// tests used to do, and it hid the fact that the check cost a round-trip per commit.</para>
    /// </summary>
    private void GivenStoredCommits(RepositoryId repositoryId, params CommitLineCount[] commits) =>
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId).Returns(commits.ToList());

    private static CommitLineCount StoredCommit(string sha, int linesAdded = 0, int linesRemoved = 0) => new()
    {
        Id = Guid.NewGuid(),
        CommitSha = sha,
        CommitDate = DateTime.UtcNow,
        LinesAdded = linesAdded,
        LinesRemoved = linesRemoved
    };

    [Fact]
    public async Task Handle_RepoNotFound_ReturnsUnitWithoutProcessing()
    {
        var repoId = RepositoryId.New();
        _dataService.GetRepositoryByIdAsync(repoId).Returns((GitHubRepository?)null);

        var result = await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        result.Should().Be(MediatR.Unit.Value);
        await _gitHubService.DidNotReceive().CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _gitHubService.DidNotReceive().PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_NewRepo_ClonesRepository()
    {
        var repoId = RepositoryId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LocalPath = "" // Empty — triggers clone
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("cloned-path");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _gitHubService.Received(1).CloneRepositoryAsync(repo.CloneUrl, $"repo_{repoId}", Arg.Any<string?>());
        await _gitHubService.DidNotReceive().PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_ExistingLocalPath_PullsWithTheOwningUsersToken()
    {
        var repoId = RepositoryId.New();
        var userId = UserId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LocalPath = "/existing/path", // Has local path — triggers pull
            UserId = userId
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _userService.GetUserByIdAsync(userId).Returns(new User
        {
            Id = userId,
            GitHubId = "12345",
            Username = "tester",
            AccessToken = "ghp_test_token"
        });
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>())
            .Returns("pulled");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _prefsService.GetFileExtensionsAsync(userId).Returns(new List<string> { ".cs", ".ts" });

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _gitHubService.Received(1).PullRepositoryAsync("/existing/path", "ghp_test_token");
        await _gitHubService.DidNotReceive().CloneRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _prefsService.Received(1).GetFileExtensionsAsync(userId);
    }

    [Fact]
    public async Task Handle_ClearExistingData_DeletesCommitsAndResetsDate()
    {
        var repoId = RepositoryId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "o",
            Name = "n",
            CloneUrl = "url",
            LocalPath = "/path",
            LastAnalyzedCommitDate = DateTime.UtcNow
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());

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
        var repoId = RepositoryId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "o",
            Name = "n",
            CloneUrl = "url",
            LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "abc123", CommitDate = DateTime.UtcNow, LinesAdded = 50, LinesRemoved = 10 }
        };
        var lineCounts = new Dictionary<string, int> { { ".cs", 100 }, { ".js", 50 } };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        GivenStoredCommits(repoId);
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "abc123", Arg.Any<IEnumerable<string>>())
            .Returns(lineCounts);

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
        var repoId = RepositoryId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "o",
            Name = "n",
            CloneUrl = "url",
            LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "existing-sha", CommitDate = DateTime.UtcNow, LinesAdded = 10, LinesRemoved = 5 }
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        GivenStoredCommits(repoId, StoredCommit("existing-sha", linesAdded: 10, linesRemoved: 5));

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        // Should NOT count lines since commit already exists and ForceReanalysis=false
        await _gitHubService.DidNotReceive().CountLinesInCommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
        await _dataService.DidNotReceive().AddCommitLineCountAsync(Arg.Any<CommitLineCount>());
    }

    [Fact]
    public async Task Handle_ContinuesProcessingAfterCommitFailure()
    {
        var repoId = RepositoryId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "o",
            Name = "n",
            CloneUrl = "url",
            LocalPath = "/path"
        };
        var commitStats = new List<CommitStatsDto>
        {
            new() { Sha = "fail-sha", CommitDate = DateTime.UtcNow, LinesAdded = 0, LinesRemoved = 0 },
            new() { Sha = "good-sha", CommitDate = DateTime.UtcNow, LinesAdded = 10, LinesRemoved = 5 }
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>()).Returns(commitStats);
        GivenStoredCommits(repoId);

        // First commit fails, second succeeds
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "fail-sha", Arg.Any<IEnumerable<string>>())
            .ThrowsAsync(new Exception("boom"));
        _gitHubService.CountLinesInCommitAsync(Arg.Any<string>(), "good-sha", Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, int> { { ".cs", 10 } });

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        // The good commit should still be saved
        await _dataService.Received(1).AddCommitLineCountAsync(Arg.Is<CommitLineCount>(c => c.CommitSha == "good-sha"));
    }

    [Fact]
    public async Task Handle_UserWithoutToken_FallsBackToConfiguredGitHubPat()
    {
        // A user row with an empty stored token must not send an empty credential to git —
        // the handler falls back to the server-configured GitHub:PAT.
        var repoId = RepositoryId.New();
        var userId = UserId.New();
        var repo = new GitHubRepository
        {
            Id = repoId,
            Owner = "o",
            Name = "n",
            CloneUrl = "url",
            LocalPath = "/path",
            UserId = userId
        };

        _dataService.GetRepositoryByIdAsync(repoId).Returns(repo);
        _userService.GetUserByIdAsync(userId).Returns(new User
        {
            Id = userId,
            GitHubId = "12345",
            Username = "tokenless",
            AccessToken = ""
        });
        _configuration["GitHub:PAT"].Returns("ghp_server_side_pat");
        _gitHubService.IsRepositoryValidAsync(Arg.Any<string>()).Returns(true);
        _gitHubService.PullRepositoryAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("ok");
        _gitHubService.GetCommitStatsAsync(Arg.Any<string>(), Arg.Any<DateTime?>())
            .Returns(Enumerable.Empty<CommitStatsDto>());
        _prefsService.GetFileExtensionsAsync(userId).Returns(new List<string>());

        await _sut.Handle(new AnalyzeRepositoryCommitsCommand(repoId), CancellationToken.None);

        await _gitHubService.Received(1).PullRepositoryAsync("/path", "ghp_server_side_pat");
    }
}
