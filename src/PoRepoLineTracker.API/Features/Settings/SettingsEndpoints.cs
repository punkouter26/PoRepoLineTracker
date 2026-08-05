using Serilog;

namespace PoRepoLineTracker.API.Features.Settings;

internal static class SettingsEndpoints
{
    internal static void MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Rule 3.1 — /api/settings is entirely per-user state, so the group requires auth.
        // Under the FallbackPolicy these routes would be protected anyway; declaring it on the
        // group makes the intent explicit and survives a future change to the fallback.
        var settings = endpoints.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        // User preferences are the whole slice. Four sibling routes were removed because nothing
        // called them: /file-extensions and /chart/max-lines (config echoes the client never read),
        // /user-extensions (the analysis handler reads preferences directly through
        // IUserPreferencesService), and the anonymous /api/feature-flags — whose comments claimed
        // "the layout calls this on every page load" long after the layout had stopped.

        settings.MapGet("/user-preferences", async (HttpContext ctx, IUserPreferencesService preferencesService) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                var preferences = await preferencesService.GetPreferencesAsync(userId);
                return Results.Ok(preferences);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving user preferences");
                return Results.Problem($"Error retrieving user preferences: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetUserPreferences");

        settings.MapPut("/user-preferences", async (HttpContext ctx, IUserPreferencesService preferencesService, UserPreferences preferences) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                preferences = preferences with { UserId = userId, LastUpdated = DateTime.UtcNow };
                await preferencesService.SavePreferencesAsync(preferences);
                return Results.Ok(preferences);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving user preferences");
                return Results.Problem($"Error saving user preferences: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("SaveUserPreferences");
    }
}
