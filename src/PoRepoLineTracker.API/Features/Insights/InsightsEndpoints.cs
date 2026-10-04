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
        insights.MapGet("/portfolio", async (HttpContext ctx, GetPortfolioInsightsQueryHandler portfolioHandler) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            try
            {
                var result = await portfolioHandler.Handle(new GetPortfolioInsightsQuery(userId));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error building portfolio insights for user {UserId}", userId);
                return Results.Problem($"Error building portfolio insights.",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("GetPortfolioInsights");

        // Deliberately does NOT record the visit. Reading the digest and marking it read are
        // separate calls so that a page refresh, a prefetch, or a second tab cannot silently
        // collapse the window to nothing before the user has seen the banner — the client marks
        // seen only once it has actually rendered one.
        insights.MapGet("/digest", async (HttpContext ctx, GetWeeklyDigestQueryHandler digestHandler, IUserPreferencesService preferencesService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            try
            {
                var preferences = await preferencesService.GetPreferencesAsync(userId);
                var result = await digestHandler.Handle(new GetWeeklyDigestQuery(userId, preferences.LastSeenUtc));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error building digest for user {UserId}", userId);
                return Results.Problem($"Error building digest.",
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
                return Results.Problem($"Error recording visit.",
                    statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("MarkDigestSeen");

        // Whether the optional Claude features exist on this deployment, so the client can leave
        // their controls out entirely rather than render buttons that answer "not configured".
        insights.MapGet("/assistant", (ClaudeAssistant assistant) =>
            Results.Ok(new AiStatusDto { Available = assistant.IsAvailable }))
        .WithName("GetAssistantStatus");

        // One or two sentences reading the digest's figures. Only numbers are sent: repository
        // names are user-controlled text, so the busiest repositories go as R1..R3 and the names
        // are put back here, after the model has answered.
        insights.MapGet("/digest/narrative", async (
            HttpContext ctx,
            GetWeeklyDigestQueryHandler digestHandler,
            IUserPreferencesService preferencesService,
            ClaudeAssistant assistant,
            CancellationToken cancellationToken) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            if (!assistant.IsAvailable)
                return Results.Ok(new DigestNarrativeDto());

            var preferences = await preferencesService.GetPreferencesAsync(userId);
            var digest = await digestHandler.Handle(new GetWeeklyDigestQuery(userId, preferences.LastSeenUtc));
            if (!digest.HasActivity)
                return Results.Ok(new DigestNarrativeDto());

            var figures = System.Text.Json.JsonSerializer.Serialize(new
            {
                days = Math.Max(1, (int)Math.Round((digest.UntilUtc - digest.SinceUtc).TotalDays)),
                commits = digest.Commits,
                previousCommits = digest.PreviousCommits,
                linesAdded = digest.LinesAdded,
                previousLinesAdded = digest.PreviousLinesAdded,
                linesRemoved = digest.LinesRemoved,
                netGrowth = digest.NetGrowth,
                activeDays = digest.ActiveDays,
                streakDays = digest.CurrentStreakDays,
                repositoriesTouched = digest.ReposTouched,
                busiest = digest.TopRepos.Take(3).Select((r, i) => new { id = $"R{i + 1}", commits = r.Commits, netGrowth = r.NetGrowth })
            });

            var reply = await assistant.AskJsonAsync(
                system: "You write a one or two sentence summary of a developer's recent coding activity for a dashboard banner. "
                        + "Use only the figures in the JSON you are given. Refer to repositories by their id exactly as given (R1, R2, R3). "
                        + "Lead with what changed against the previous period when that is notable. Plain sentences, no markdown, no greeting, at most 45 words.",
                user: figures,
                schema: ClaudeAssistant.ObjectSchema(new { summary = new { type = "string" } }, "summary"),
                cancellationToken);

            var text = reply is { } json && json.TryGetProperty("summary", out var summary) ? summary.GetString() : null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 600)
                return Results.Ok(new DigestNarrativeDto());

            // Highest id first so "R1" cannot match inside "R10".
            for (var i = Math.Min(3, digest.TopRepos.Count); i >= 1; i--)
                text = text.Replace($"R{i}", digest.TopRepos[i - 1].Name, StringComparison.Ordinal);

            return Results.Ok(new DigestNarrativeDto { Text = text });
        })
        .RequireRateLimiting(RateLimitPolicies.Assistant)
        .WithName("GetDigestNarrative");
    }
}
