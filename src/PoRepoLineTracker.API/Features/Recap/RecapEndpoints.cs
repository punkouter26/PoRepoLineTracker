using MediatR;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.API.Features.Recap;

internal static class RecapEndpoints
{
    /// <summary>
    /// Earliest year the route will accept. Git itself predates nothing before this, and the
    /// bound exists so a typo in the URL is a 400 rather than a full scan of every commit against
    /// a window that cannot contain any.
    /// </summary>
    private const int EarliestYear = 1970;

    internal static void MapRecapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var recap = endpoints.MapGroup("/api/recap")
            .WithTags("Recap")
            .RequireAuthorization();

        // Two routes rather than an optional query parameter: /api/recap is "the current year",
        // which is what the page requests on load and what a shared link should keep meaning as
        // years pass, while /api/recap/2024 is a fixed year that must not drift.
        recap.MapGet("/", (HttpContext ctx, IMediator mediator) => BuildAsync(ctx, mediator, null))
            .WithName("GetCurrentYearRecap");

        recap.MapGet("/{year:int}", (int year, HttpContext ctx, IMediator mediator) => BuildAsync(ctx, mediator, year))
            .WithName("GetYearRecap");
    }

    private static async Task<IResult> BuildAsync(HttpContext ctx, IMediator mediator, int? year)
    {
        if (!ctx.User.TryGetUserId(out var userId))
            return Results.Unauthorized();

        // Bounded above by next year rather than by this one: a commit can legitimately carry a
        // timestamp slightly ahead of the server's clock, and refusing to render the year that
        // contains it would be a confusing way to report a clock skew.
        if (year is { } requested && (requested < EarliestYear || requested > DateTime.UtcNow.Year + 1))
            return Results.BadRequest(new ErrorResponse
            {
                Title = "Invalid year",
                Detail = $"Year must be between {EarliestYear} and {DateTime.UtcNow.Year + 1}.",
                Status = (int)HttpStatusCode.BadRequest
            });

        try
        {
            var result = await mediator.Send(new GetYearInCodeQuery(userId, year));
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error building recap for user {UserId} year {Year}", userId, year);
            return Results.Problem($"Error building recap: {ex.Message}",
                statusCode: (int)HttpStatusCode.InternalServerError);
        }
    }
}
