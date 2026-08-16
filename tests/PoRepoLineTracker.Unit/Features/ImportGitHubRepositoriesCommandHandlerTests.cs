using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Features.Repositories;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The Insights dashboard calls this on every load, so its guard is what stands between a page
/// visit and cloning an entire GitHub account.
///
/// <para>The token rule is the property worth pinning: the import must use the signed-in user's
/// OWN stored token and never the server-wide GitHub:PAT fallback. Without it a dev/test principal
/// — which has no user row at all — imported everything the server PAT could see, which both files
/// one person's repositories under another's account and, when the E2E UI tier opened /insights,
/// started a dozen real clones and took the app down.</para>
/// </summary>
public sealed class ImportGitHubRepositoriesCommandHandlerTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IGitHubService _gitHubService = Substitute.For<IGitHubService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ImportGitHubRepositoriesCommandHandler _sut;
    private readonly UserId _userId = UserId.New();

    public ImportGitHubRepositoriesCommandHandlerTests()
    {
        _sut = new ImportGitHubRepositoriesCommandHandler(
            _userService, _gitHubService, _mediator,
            Substitute.For<ILogger<ImportGitHubRepositoriesCommandHandler>>());
    }

    [Theory]
    [InlineData(null)]   // no user row at all — the dev/test principal
    [InlineData("")]     // user exists but never completed a GitHub sign-in
    public async Task Handle_WithoutTheUsersOwnToken_ImportsNothingAndNeverCallsGitHub(string? accessToken)
    {
        _userService.GetUserByIdAsync(_userId).Returns(accessToken is null
            ? (User?)null
            : new User { Id = _userId, GitHubId = "12345", Username = "tester", AccessToken = accessToken });

        var result = await _sut.Handle(new ImportGitHubRepositoriesCommand(_userId), CancellationToken.None);

        result.Added.Should().BeEmpty();
        await _gitHubService.DidNotReceive().GetUserRepositoriesAsync(Arg.Any<string>());
        await _mediator.DidNotReceive().Send(Arg.Any<AddMultipleRepositoriesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithTheUsersOwnToken_TracksEveryRepositoryOnTheAccount()
    {
        _userService.GetUserByIdAsync(_userId).Returns(new User
        {
            Id = _userId,
            GitHubId = "12345",
            Username = "tester",
            AccessToken = "ghp_users_own_token"
        });

        _gitHubService.GetUserRepositoriesAsync("ghp_users_own_token").Returns(new[]
        {
            new GitHubUserRepositoryDto { Owner = "punkouter26", Name = "PoRepoLineTracker", CloneUrl = "https://github.com/punkouter26/PoRepoLineTracker.git" },
            new GitHubUserRepositoryDto { Owner = "punkouter26", Name = "PoSumo", CloneUrl = "https://github.com/punkouter26/PoSumo.git" },
            new GitHubUserRepositoryDto { Owner = "", Name = "broken", CloneUrl = "" } // no owner — not trackable
        });

        _mediator.Send(Arg.Any<AddMultipleRepositoriesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BulkAddResult());

        await _sut.Handle(new ImportGitHubRepositoriesCommand(_userId), CancellationToken.None);

        // Every usable repository is forwarded to the single write path, which dedupes — the
        // entry missing an owner is dropped rather than stored as a broken row.
        await _mediator.Received(1).Send(
            Arg.Is<AddMultipleRepositoriesCommand>(c =>
                c.UserId == _userId &&
                c.Repositories.Count() == 2 &&
                c.Repositories.Any(r => r.RepoName == "PoRepoLineTracker") &&
                c.Repositories.Any(r => r.RepoName == "PoSumo")),
            Arg.Any<CancellationToken>());
    }
}
