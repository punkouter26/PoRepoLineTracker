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

        github.MapGet("/user-repositories", async (
            HttpContext ctx,
            IGitHubService githubService,
            IUserService userService,
            IConfiguration config,
            HybridCache cache,
            IWebHostEnvironment env,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                var user = await userService.GetUserByIdAsync(userId);

                // GitHub OAuth is the only provider, so the stored token is a GitHub token.
                // Fall back to the server-configured PAT for rows with a missing/empty token.
                var accessToken = !string.IsNullOrEmpty(user?.AccessToken)
                    ? user.AccessToken
                    : config[ConfigKeys.GitHub.Pat];

                if (string.IsNullOrEmpty(accessToken))
                {
                    if (env.IsDevelopment())
                    {
                        var devRepos = new List<GitHubUserRepositoryDto>
                        {
                            new()
                            {
                                Name = "PoRepoLineTracker",
                                Owner = "punkouter26",
                                FullName = "punkouter26/PoRepoLineTracker",
                                CloneUrl = "https://github.com/punkouter26/PoRepoLineTracker.git",
                                Description = "Track lines of code across all your GitHub repositories over time",
                                IsPrivate = false,
                                Language = "C#"
                            },
                            new()
                            {
                                Name = "Hello-World",
                                Owner = "octocat",
                                FullName = "octocat/Hello-World",
                                CloneUrl = "https://github.com/octocat/Hello-World.git",
                                Description = "My first repository on GitHub!",
                                IsPrivate = false,
                                Language = "Text"
                            },
                            new()
                            {
                                Name = "Spoon-Knife",
                                Owner = "octocat",
                                FullName = "octocat/Spoon-Knife",
                                CloneUrl = "https://github.com/octocat/Spoon-Knife.git",
                                Description = "This repo is for spooning and knifing.",
                                IsPrivate = false,
                                Language = "HTML"
                            }
                        };
                        return Results.Ok(devRepos);
                    }

                    // No GitHub credential available at all: GitHub sign-in gives a per-user token;
                    // otherwise a server GitHub:PAT must be configured in Key Vault.
                    return Results.Problem(
                        "No GitHub access available. Sign in with GitHub, or configure a GitHub Personal Access Token (GitHub:PAT) in Key Vault.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                // Cached per user, not per token: the token can rotate (PAT fallback vs
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
