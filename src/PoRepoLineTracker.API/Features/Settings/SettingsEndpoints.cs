using MediatR;
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

        settings.MapGet("/file-extensions", async (IMediator mediator) =>
        {
            try
            {
                var extensions = await mediator.Send(new GetConfiguredFileExtensionsQuery());
                return Results.Ok(extensions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving configured file extensions.");
                return Results.Problem($"Error retrieving configured file extensions: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetConfiguredFileExtensions");

        settings.MapGet("/chart/max-lines", (IConfiguration configuration) =>
            Results.Ok(configuration.GetValue<int>(ConfigKeys.ChartSettings.MaxLinesOfCode, 50000)))
        .WithName("GetChartMaxLines");

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

        settings.MapGet("/user-extensions", async (HttpContext ctx, IUserPreferencesService preferencesService) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                var extensions = await preferencesService.GetFileExtensionsAsync(userId);
                return Results.Ok(extensions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving user file extensions");
                return Results.Problem($"Error retrieving user file extensions: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetUserFileExtensions");

        // Public endpoint: exposes client-visible feature flags so the Blazor WASM app
        // can adapt its UI without requiring additional auth. Strategy Pattern (GoF):
        // flags act as runtime strategy selectors.
        endpoints.MapGet("/api/feature-flags", (IConfiguration configuration) =>
        {
            var flags = new
            {
                EnableGitHubApi = configuration.GetValue<bool>(ConfigKeys.FeatureFlags.EnableGitHubApi),
                EnableBackgroundAnalysis = configuration.GetValue<bool>(ConfigKeys.FeatureFlags.EnableBackgroundAnalysis),
                EnableOpenTelemetryExport = configuration.GetValue<bool>(ConfigKeys.FeatureFlags.EnableOpenTelemetryExport)
            };
            return Results.Ok(flags);
        })
        .AllowAnonymous()
        .WithName("GetFeatureFlags");
    }
}
