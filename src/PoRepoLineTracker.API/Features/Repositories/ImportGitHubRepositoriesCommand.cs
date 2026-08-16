using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>
/// Tracks every repository on the signed-in user's GitHub account, so the Insights dashboard can
/// honestly call itself "Global". Previously it summed only what had been added by hand.
/// </summary>
public record ImportGitHubRepositoriesCommand(UserId UserId) : IRequest<BulkAddResult>;

public sealed class ImportGitHubRepositoriesCommandHandler(
    IUserService userService,
    IGitHubService gitHubService,
    IMediator mediator,
    ILogger<ImportGitHubRepositoriesCommandHandler> logger)
    : IRequestHandler<ImportGitHubRepositoriesCommand, BulkAddResult>
{
    public async Task<BulkAddResult> Handle(ImportGitHubRepositoriesCommand request, CancellationToken cancellationToken)
    {
        var user = await userService.GetUserByIdAsync(request.UserId);

        // The user's OWN OAuth token, never the server-wide GitHub:PAT fallback that
        // /api/github/user-repositories accepts. Two reasons, and both are load-bearing:
        //
        //   Correctness — this imports repositories INTO a user's account. Falling back to the
        //   server PAT would file the PAT owner's repositories under whichever user happened to
        //   open the page.
        //
        //   Blast radius — a dev/test principal (FakeAuthHandler) has no stored token, so it
        //   imports nothing. Without this guard, merely opening /insights in the E2E UI tier
        //   cloned every real repository the PAT could see and took the app down with it.
        if (string.IsNullOrEmpty(user?.AccessToken))
        {
            logger.LogInformation(
                "GitHub import skipped for user {UserId}: no GitHub token of their own is stored.",
                request.UserId);
            return new BulkAddResult();
        }

        var gitHubRepositories = (await gitHubService.GetUserRepositoriesAsync(user.AccessToken)).ToList();

        var toTrack = gitHubRepositories
            .Where(r => !string.IsNullOrWhiteSpace(r.Owner) && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new BulkRepositoryDto { Owner = r.Owner, RepoName = r.Name, CloneUrl = r.CloneUrl })
            .ToList();

        logger.LogInformation(
            "GitHub import for user {UserId}: {Count} repositories on the account.",
            request.UserId, toTrack.Count);

        if (toTrack.Count == 0)
            return new BulkAddResult();

        // Reuses the one write path, which dedupes on owner+name — so running this on every visit
        // re-imports nothing and only genuinely new repositories come back in Added.
        return await mediator.Send(new AddMultipleRepositoriesCommand(toTrack, request.UserId), cancellationToken);
    }
}
