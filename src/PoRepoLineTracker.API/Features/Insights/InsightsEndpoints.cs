using MediatR;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.API.Features.Insights;

internal static class InsightsEndpoints
{
    internal static void MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var insights = endpoints.MapGroup("/api/insights")
            .WithTags("Insights")
            .RequireAuthorization();

        // No IDOR check of the kind the repository routes carry: the query is scoped by the
        // caller's own user id rather than by a supplied repository id, so there is no identifier
        // here for a caller to substitute.
        insights.MapGet("/portfolio", async (HttpContext ctx, IMediator mediator) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            try
            {
                var result = await mediator.Send(new GetPortfolioInsightsQuery(userId));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error building portfolio insights for user {UserId}", userId);
                return Results.Problem($"Error building portfolio insights: {ex.Message}",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("GetPortfolioInsights");
    }
}
