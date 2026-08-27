using MediatR;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.API.Features.CodeHealth;

internal static class CodeHealthEndpoints
{
    internal static void MapCodeHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var health = endpoints.MapGroup("/api/code-health")
            .WithTags("CodeHealth")
            .RequireAuthorization();

        // Unlike the portfolio routes, this one takes a repository id from the URL — so it needs
        // the ownership guard. Without it any signed-in caller could read a report (paths,
        // hotspots, file sizes) for someone else's private repository.
        health.MapGet("/{repositoryId}", async (RepositoryId repositoryId, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var (_, error) = await RepositoryOwnership.AuthorizeAsync(repoDataService, repositoryId, userId, "read code health for");
            if (error != null) return error;

            try
            {
                var report = await mediator.Send(new GetCodeHealthQuery(repositoryId));

                // Null means the repository has never been analysed. A 404 rather than an empty
                // report: a page of zeroes is indistinguishable from genuinely terrible code, and
                // the client renders a "run an analysis first" state off this.
                return report is null
                    ? Results.NotFound(new ErrorResponse
                    {
                        Title = "Nothing to measure",
                        Detail = "This repository has no analysed commits yet. Run an analysis and try again.",
                        Code = "no_analysed_commits",
                        Status = (int)HttpStatusCode.NotFound
                    })
                    : Results.Ok(report);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error building code health for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error building code health: {ex.Message}",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("GetCodeHealth");
    }
}
