using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class SettingsEndpoints
{
    internal static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/file-extensions", async (IMediator mediator) =>
        {
            try
            {
                var extensions = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetConfiguredFileExtensionsQuery());
                return Results.Ok(extensions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving configured file extensions.");
                return Results.Problem($"Error retrieving configured file extensions: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetConfiguredFileExtensions");

        app.MapGet("/api/settings/chart/max-lines", (IConfiguration configuration) =>
            Results.Ok(configuration.GetValue<int>("ChartSettings:MaxLinesOfCode", 50000)))
        .WithName("GetChartMaxLines");

        app.MapGet("/api/settings/user-preferences", async (HttpContext ctx, IUserPreferencesService preferencesService) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
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
        .RequireAuthorization()
        .WithName("GetUserPreferences");

        app.MapPut("/api/settings/user-preferences", async (HttpContext ctx, IUserPreferencesService preferencesService, UserPreferences preferences) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
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
        .RequireAuthorization()
        .WithName("SaveUserPreferences");

        app.MapGet("/api/settings/user-extensions", async (HttpContext ctx, IUserPreferencesService preferencesService) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
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
        .RequireAuthorization()
        .WithName("GetUserFileExtensions");
    }
}
