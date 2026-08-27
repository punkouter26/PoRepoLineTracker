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
        // here for a caller to substitute. The same holds for both digest routes below.
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

        // Deliberately does NOT record the visit. Reading the digest and marking it read are
        // separate calls so that a page refresh, a prefetch, or a second tab cannot silently
        // collapse the window to nothing before the user has seen the banner — the client marks
        // seen only once it has actually rendered one.
        insights.MapGet("/digest", async (HttpContext ctx, IMediator mediator, IUserPreferencesService preferencesService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            try
            {
                var preferences = await preferencesService.GetPreferencesAsync(userId);
                var result = await mediator.Send(new GetWeeklyDigestQuery(userId, preferences.LastSeenUtc));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error building digest for user {UserId}", userId);
                return Results.Problem($"Error building digest: {ex.Message}",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("GetWeeklyDigest");

        // Read-modify-write rather than a targeted column update: SavePreferencesAsync upserts
        // with TableUpdateMode.Replace, so writing a preferences object built from anything less
        // than the stored row would blank the user's counted-extensions list.
        insights.MapPost("/digest/seen", async (HttpContext ctx, IUserPreferencesService preferencesService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            try
            {
                var preferences = await preferencesService.GetPreferencesAsync(userId);
                await preferencesService.SavePreferencesAsync(preferences with { LastSeenUtc = DateTime.UtcNow });
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error recording digest visit for user {UserId}", userId);
                return Results.Problem($"Error recording visit: {ex.Message}",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("MarkDigestSeen");
    }
}
