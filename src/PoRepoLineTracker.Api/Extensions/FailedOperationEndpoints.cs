using PoRepoLineTracker.Application.Interfaces;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class FailedOperationEndpoints
{
    internal static void MapFailedOperationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/failed-operations", async (IFailedOperationService failedOperationService) =>
        {
            try
            {
                var failedOperations = await failedOperationService.GetAllFailedOperationsAsync();
                Log.Information("Retrieved {Count} total failed operations", failedOperations.Count());
                return Results.Ok(failedOperations);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving all failed operations");
                return Results.Problem($"Error retrieving failed operations: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("GetAllFailedOperations");

        app.MapGet("/api/failed-operations/{repositoryId}", async (Guid repositoryId, IFailedOperationService failedOperationService) =>
        {
            try
            {
                var failedOperations = await failedOperationService.GetFailedOperationsByRepositoryIdAsync(repositoryId);
                return Results.Ok(failedOperations);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving failed operations for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving failed operations: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("GetFailedOperationsByRepository");

        app.MapDelete("/api/failed-operations/{failedOperationId}", async (Guid failedOperationId, IFailedOperationService failedOperationService) =>
        {
            try
            {
                await failedOperationService.DeleteFailedOperationAsync(failedOperationId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting failed operation {FailedOperationId}", failedOperationId);
                return Results.Problem($"Error deleting failed operation: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("DeleteFailedOperation");
    }
}
