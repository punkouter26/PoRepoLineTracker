using PoRepoLineTracker.Application.Interfaces;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class GitHubEndpoints
{
    internal static void MapGitHubEndpoints(this WebApplication app)
    {
        app.MapGet("/api/github/user-repositories", async (HttpContext ctx, IGitHubService githubService, IUserService userService) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return Results.Unauthorized();

                var accessToken = await userService.GetAccessTokenAsync(userId);
                if (string.IsNullOrEmpty(accessToken))
                    return Results.Unauthorized();

                var userRepositories = await githubService.GetUserRepositoriesAsync(accessToken);
                return Results.Ok(userRepositories);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning("Authentication error: {ErrorMessage}", ex.Message);
                return Results.BadRequest($"Authentication error: {ex.Message}");
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
