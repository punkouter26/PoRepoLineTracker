using PoRepoLineTracker.Application.Interfaces;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class GitHubEndpoints
{
    internal static void MapGitHubEndpoints(this WebApplication app)
    {
        app.MapGet("/api/github/user-repositories", async (HttpContext ctx, IGitHubService githubService, IUserService userService, IConfiguration config) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
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

                var gitHubPat = config["GitHub:PAT"];
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

                var userRepositories = await githubService.GetUserRepositoriesAsync(accessToken);
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
        .RequireAuthorization()
        .WithName("GetUserRepositories");
    }
}
