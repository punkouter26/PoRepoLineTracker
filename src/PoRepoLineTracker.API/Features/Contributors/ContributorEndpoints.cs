using MediatR;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.API.Features.Contributors;

internal static class ContributorEndpoints
{
    internal static void MapContributorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Shares the /api/repositories prefix with RepositoryEndpoints but stays its own group so
        // this route carries its own OpenAPI tag.
        var contributors = endpoints.MapGroup("/api/repositories")
            .WithTags("Contributors")
            .RequireAuthorization();

        // Get top contributors by lines of code
        contributors.MapGet("/{repositoryId}/contributors/{days}", async (RepositoryId repositoryId, int days, int topN, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read contributors for repo {RepositoryId} owned by {OwnerId}",
                    userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            try
            {
                var contributorStats = await mediator.Send(new GetContributorStatsQuery(repositoryId, days, topN > 0 ? topN : 10));
                return Results.Ok(contributorStats);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving contributor stats for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving contributor stats: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("GetContributorStats");
    }
}
