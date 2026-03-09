using MediatR;
using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class RepositoryEndpoints
{
    internal static void MapRepositoryEndpoints(this WebApplication app)
    {
        // #6 fix: added RequireAuthorization() - was unprotected
        app.MapPost("/api/repositories", async (GitHubRepository newRepo, HttpContext ctx, IMediator mediator) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var repo = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand(newRepo.Owner, newRepo.Name, newRepo.CloneUrl, userId));
            return Results.Created($"/api/repositories/{repo.Id}", repo);
        })
        .RequireAuthorization()
        .WithName("AddRepository");

        app.MapGet("/api/repositories", async (HttpContext ctx, IMediator mediator) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var repositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesQuery(userId));
            return Results.Ok(repositories);
        })
        .RequireAuthorization()
        .WithName("GetAllRepositories");

        // #6 fix: added RequireAuthorization() - was unprotected
        app.MapGet("/api/repositories/{repositoryId}/linehistory/{days}", async (Guid repositoryId, int days, IMediator mediator) =>
        {
            try
            {
                var lineHistory = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetLineCountHistoryQuery(repositoryId, days));
                return Results.Ok(lineHistory);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving line count history for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetRepositoryLineHistory");

        app.MapGet("/api/repositories/allcharts/{days}", async (int days, HttpContext ctx, IMediator mediator) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return Results.Unauthorized();

                var data = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesLineCountHistoryQuery(days, userId));
                return Results.Ok(data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving line count history for all repositories");
                return Results.Problem($"Error retrieving all repositories line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetAllRepositoriesLineHistory");

        // #6 fix: added RequireAuthorization() + ownership check - was fully unprotected
        app.MapDelete("/api/repositories/{repositoryId}", async (Guid repositoryId, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            // Ownership guard: only the owning user may delete their repository
            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null)
                return Results.NotFound($"Repository with ID {repositoryId} not found.");

            if (existing.UserId != userId)
            {
                Log.Warning("Unauthorized delete attempt: User {UserId} tried to delete repository {RepositoryId} owned by {OwnerId}",
                    userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            try
            {
                await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.DeleteRepositoryCommand(repositoryId));
                Log.Information("Repository {RepositoryId} deleted successfully via API.", repositoryId);
                return Results.NoContent();
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound($"Repository with ID {repositoryId} not found.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error deleting repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("DeleteRepository");

        app.MapDelete("/api/repositories/all", async (HttpContext ctx, IMediator mediator) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    Log.Warning("Remove all repositories failed: No valid UserId claim found");
                    return Results.Unauthorized();
                }

                Log.Information("Starting removal of all repositories for user {UserId}", userId);
                await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.RemoveAllRepositoriesCommand(userId));
                Log.Information("All repositories for user {UserId} removed successfully via API.", userId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing all repositories: {ErrorType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
                return Results.Problem($"Error removing all repositories: {ex.GetType().Name} - {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("RemoveAllRepositories");

        app.MapPost("/api/repositories/bulk", async ([FromBody] IEnumerable<BulkRepositoryDto> repositories, HttpContext ctx, IMediator mediator, IServiceScopeFactory scopeFactory) =>
        {
            try
            {
                var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return Results.Unauthorized();

                Log.Information("=== BULK REPOSITORY ADD ENDPOINT CALLED ===");
                var repoList = repositories?.ToList() ?? [];
                Log.Information("Number of repositories in request: {Count}", repoList.Count);

                for (int i = 0; i < repoList.Count; i++)
                {
                    var repo = repoList[i];
                    Log.Information("API Request Repo [{Index}]: Owner='{Owner}', RepoName='{RepoName}', CloneUrl='{CloneUrl}'",
                        i, repo?.Owner ?? "NULL", repo?.RepoName ?? "NULL", repo?.CloneUrl ?? "NULL");
                }

                Log.Information("Sending AddMultipleRepositoriesCommand to MediatR with {Count} repositories for user {UserId}", repoList.Count, userId);
                var addedRepositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddMultipleRepositoriesCommand(
                    repoList, userId));

                var addedList = addedRepositories.ToList();
                Log.Information("MediatR returned {Count} repositories: {Repos}", addedList.Count,
                    string.Join(", ", addedList.Select(r => $"{r.Owner}/{r.Name}")));

                // Fire analysis in the background so the HTTP response returns immediately.
                // Cloning + line-counting can take several minutes — doing it synchronously
                // causes Blazor WASM's 100 s HttpClient timeout to fire.
                var newRepoIds = addedList
                    .Where(r => r.LastAnalyzedCommitDate == null)
                    .Select(r => r.Id)
                    .ToList();

                if (newRepoIds.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var bgMediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                        foreach (var repoId in newRepoIds)
                        {
                            try
                            {
                                Log.Information("Background: starting analysis for repo {RepoId}", repoId);
                                await bgMediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(repoId));
                                Log.Information("Background: analysis complete for repo {RepoId}", repoId);
                            }
                            catch (Exception bgEx)
                            {
                                Log.Error(bgEx, "Background: analysis failed for repo {RepoId}: {Message}", repoId, bgEx.Message);
                            }
                        }
                    });
                }

                return Results.Ok(addedList);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EXCEPTION in bulk repository endpoint: {Message}. Stack: {StackTrace}", ex.Message, ex.StackTrace);
                return Results.Problem($"Error adding repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("AddMultipleRepositories");

        // #6 fix: added RequireAuthorization() - was unprotected
        app.MapPost("/api/repositories/{repositoryId}/analyses", async (Guid repositoryId, [FromQuery] bool force, HttpContext ctx, IServiceScopeFactory scopeFactory) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out _))
                return Results.Unauthorized();

            Log.Information("Background analysis queued for repository {RepositoryId} (force={Force})", repositoryId, force);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var bgMediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                try
                {
                    await bgMediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(repositoryId, ForceReanalysis: force));
                    Log.Information("Background analysis completed for repository {RepositoryId}", repositoryId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background analysis failed for repository {RepositoryId}", repositoryId);
                }
            });
            return Results.Accepted();
        })
        .RequireAuthorization()
        .WithName("CreateRepositoryAnalysis");

        app.MapPost("/api/repositories/{repositoryId}/reanalyze", async (Guid repositoryId, HttpContext ctx, IServiceScopeFactory scopeFactory) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            Log.Information("Background re-analysis queued for repository {RepositoryId} by user {UserId}", repositoryId, userId);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var bgMediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                try
                {
                    await bgMediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(
                        repositoryId, ForceReanalysis: false, ClearExistingData: true));
                    Log.Information("Background re-analysis completed for repository {RepositoryId}", repositoryId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background re-analysis failed for repository {RepositoryId}", repositoryId);
                }
            });
            return Results.Accepted(value: new { message = "Re-analysis started. All commit data will be re-calculated with your current file extension preferences." });
        })
        .RequireAuthorization()
        .WithName("ReanalyzeRepository");

        // #6 fix: added RequireAuthorization() - was unprotected
        app.MapGet("/api/repositories/{repositoryId}/file-extension-percentages", async (Guid repositoryId, IMediator mediator) =>
        {
            try
            {
                var percentages = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetFileExtensionPercentagesQuery(repositoryId));
                return Results.Ok(percentages);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving file extension percentages for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving file extension percentages: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetFileExtensionPercentages");

        // #6 fix: added RequireAuthorization() - was unprotected
        app.MapGet("/api/repositories/{repositoryId}/top-files", async (Guid repositoryId, IMediator mediator, int count = 5) =>
        {
            try
            {
                var topFiles = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetTopFilesQuery(repositoryId, count));
                return Results.Ok(topFiles);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving top files for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving top files: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetTopFiles");
    }
}
