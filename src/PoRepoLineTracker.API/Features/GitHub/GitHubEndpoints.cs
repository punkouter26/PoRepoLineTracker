using Microsoft.Extensions.Caching.Hybrid;
using Serilog;

namespace PoRepoLineTracker.API.Features.GitHub;

internal static class GitHubEndpoints
{
    internal static void MapGitHubEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var github = endpoints.MapGroup("/api/github")
            .WithTags("GitHub")
            .RequireAuthorization();

        github.MapGet("/user-repositories", async (HttpContext ctx, IGitHubService githubService, IUserService userService, IConfiguration config, HybridCache cache, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                var user = await userService.GetUserByIdAsync(userId);

                // The user's stored OAuth token only authenticates against GitHub when they
                // signed in with GitHub. Microsoft-authenticated users (GitHubId "ms:*") hold a
                // Microsoft Graph token that GitHub rejects with 401, so fall back to the
                // configured GitHub PAT. This keeps repository listing working regardless of
                // which provider was used to sign in.
                var loggedInWithGitHub = user is not null
                    && !string.IsNullOrEmpty(user.GitHubId)
                    && !user.GitHubId.StartsWith("ms:", StringComparison.OrdinalIgnoreCase);

                var gitHubPat = config[ConfigKeys.GitHub.Pat];
                var accessToken = loggedInWithGitHub && !string.IsNullOrEmpty(user!.AccessToken)
                    ? user.AccessToken
                    : gitHubPat;

                if (string.IsNullOrEmpty(accessToken))
                {
                    // No GitHub credential available at all: GitHub sign-in gives a per-user token;
                    // otherwise a server GitHub:PAT must be configured in Key Vault.
                    return Results.Problem(
                        "No GitHub access available. Sign in with GitHub, or configure a GitHub Personal Access Token (GitHub:PAT) in Key Vault.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                // Rule 5.4 — cached per user, not per token: the token can rotate (PAT fallback vs
                // the user's own OAuth token) while the answer is the same repository list, and a
                // token in a cache key is a token written to a cache store.
                var userRepositories = await cache.GetOrCreateAsync(
                    $"github:user-repositories:{userId}",
                    (githubService, accessToken),
                    static async (state, ct) =>
                        (await state.githubService.GetUserRepositoriesAsync(state.accessToken)).ToList(),
                    cancellationToken: cancellationToken);

                return Results.Ok(userRepositories);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning("Authentication error: {ErrorMessage}", ex.Message);
                return Results.BadRequest($"Authentication error: {ex.Message}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // The GitHub credential was present but rejected (expired/revoked PAT or token).
                Log.Warning(ex, "GitHub rejected the access token (401) when listing repositories");
                return Results.Problem(
                    "GitHub rejected the credential (401). The configured GitHub PAT may be expired or missing 'repo' scope — update GitHub:PAT in Key Vault, or sign in with GitHub.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching user repositories from GitHub API");
                return Results.Problem($"Error fetching user repositories: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetUserRepositories");
    }
}
