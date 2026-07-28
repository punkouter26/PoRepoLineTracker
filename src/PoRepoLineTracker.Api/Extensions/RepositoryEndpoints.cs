using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Domain.Models;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class RepositoryEndpoints
{
    internal static void MapRepositoryEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        // Rule 3.1 — one group carries the prefix and the authorization requirement for the whole
        // slice. Every route below is authenticated because the group says so, not because each
        // endpoint remembered to say so (the "#6 fix" comments below record the era when they didn't).
        var repos = endpoints.MapGroup("/api/repositories")
            .WithTags("Repositories")
            .RequireAuthorization();

        repos.MapPost("/", async (GitHubRepository newRepo, HttpContext ctx, IMediator mediator) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var repo = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand(newRepo.Owner, newRepo.Name, newRepo.CloneUrl, userId));
            return Results.Created($"/api/repositories/{repo.Id}", repo);
        })
        .WithName("AddRepository");

        repos.MapGet("/", async (HttpContext ctx, IMediator mediator) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var repositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesQuery(userId));
            return Results.Ok(repositories);
        })
        .WithName("GetAllRepositories");

        // #6 fix: added RequireAuthorization() - was unprotected
        repos.MapGet("/{repositoryId}/linehistory/{days}", async (RepositoryId repositoryId, int days, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read linehistory for repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

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
        .WithName("GetRepositoryLineHistory");

        repos.MapGet("/allcharts/{days}", async (int days, HttpContext ctx, IMediator mediator) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
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
        .WithName("GetAllRepositoriesLineHistory");

        // #6 fix: added RequireAuthorization() + ownership check - was fully unprotected
        repos.MapDelete("/{repositoryId}", async (RepositoryId repositoryId, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
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
        .WithName("DeleteRepository");

        repos.MapDelete("/all", async (HttpContext ctx, IMediator mediator) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
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
        .WithName("RemoveAllRepositories");

        repos.MapPost("/bulk", async ([FromBody] IEnumerable<BulkRepositoryDto> repositories, HttpContext ctx, IMediator mediator, IServiceScopeFactory scopeFactory, IValidator<BulkRepositoryDto> repoValidator) =>
        {
            try
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                Log.Information("=== BULK REPOSITORY ADD ENDPOINT CALLED ===");
                var repoList = repositories?.ToList() ?? [];
                Log.Information("Number of repositories in request: {Count}", repoList.Count);

                // Rule 2.2 — validate each entry with the shared FluentValidation rules.
                foreach (var dto in repoList)
                {
                    var v = await repoValidator.ValidateAsync(dto);
                    if (!v.IsValid)
                        return Results.ValidationProblem(v.ToDictionary());
                }

                for (int i = 0; i < repoList.Count; i++)
                {
                    var repo = repoList[i];
                    Log.Information("API Request Repo [{Index}]: Owner='{Owner}', RepoName='{RepoName}', CloneUrl='{CloneUrl}'",
                        i, repo?.Owner ?? "NULL", repo?.RepoName ?? "NULL", repo?.CloneUrl ?? "NULL");
                }

                Log.Information("Sending AddMultipleRepositoriesCommand to MediatR with {Count} repositories for user {UserId}", repoList.Count, userId);
                var result = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddMultipleRepositoriesCommand(
                    repoList, userId));

                Log.Information("Bulk add: Added={Added}, AlreadyTracked={AlreadyTracked}",
                    result.Added.Count, result.AlreadyTracked.Count);

                // Fire analysis only for truly NEW repositories — already-tracked repos are already analyzed
                if (result.Added.Count > 0)
                {
                    var newRepoIds = result.Added.Select(r => r.Id).ToList();
                    _ = Task.Run(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var bgMediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                        foreach (var repoId in newRepoIds)
                        {
                            try
                            {
                                Log.Information("Background: starting analysis for new repo {RepoId}", repoId);
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

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EXCEPTION in bulk repository endpoint: {Message}. Stack: {StackTrace}", ex.Message, ex.StackTrace);
                return Results.Problem($"Error adding repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .WithName("AddMultipleRepositories");

        // #6 fix: added RequireAuthorization() - was unprotected
        repos.MapPost("/{repositoryId}/analyses", async (RepositoryId repositoryId, [FromQuery] bool force, HttpContext ctx, IServiceScopeFactory scopeFactory, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to trigger analysis for repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

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
        .WithName("CreateRepositoryAnalysis");

        repos.MapPost("/{repositoryId}/reanalyze", async (RepositoryId repositoryId, HttpContext ctx, IServiceScopeFactory scopeFactory, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to reanalyze repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

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
        .WithName("ReanalyzeRepository");

        // #6 fix: added RequireAuthorization() - was unprotected
        repos.MapGet("/{repositoryId}/file-extension-percentages", async (RepositoryId repositoryId, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read file-extension-percentages for repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

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
        .WithName("GetFileExtensionPercentages");

        // #6 fix: added RequireAuthorization() - was unprotected
        repos.MapGet("/{repositoryId}/top-files", async (RepositoryId repositoryId, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService, int count = 5) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read top-files for repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

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
        .WithName("GetTopFiles");

        // Analysis progress endpoint — returns live step/commit progress for an active analysis job.
        // Ownership check: only the owning user may read progress for their repo.
        repos.MapGet("/{repositoryId}/analysis-progress", async (RepositoryId repositoryId, HttpContext ctx, IRepositoryDataService repoDataService, PoRepoLineTracker.Application.Interfaces.IAnalysisProgressService progressService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read analysis-progress for repo {RepositoryId} owned by {OwnerId}", userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            var progress = progressService.GetProgress(repositoryId);
            if (progress == null)
                return Results.NotFound(new { message = "No active or recent analysis job found for this repository." });

            return Results.Ok(progress);
        })
        .WithName("GetRepositoryAnalysisProgress");

        // DEV-ONLY: Patch the LocalPath for a local-upload repository whose LocalPath was lost
        // before GitHubRepositoryEntity.LocalPath was added. Remove after data is fixed.
        if (isDevelopment)
        {
            var devRepos = endpoints.MapGroup("/api/dev/repositories")
                .WithTags("Dev")
                .RequireAuthorization();

            devRepos.MapPost("/{repositoryId}/fix-local-path", async (
                RepositoryId repositoryId,
                [FromQuery] string localPath,
                HttpContext ctx,
                IRepositoryDataService repoDataService) =>
            {
                if (!ctx.User.TryGetUserId(out var userId))
                    return Results.Unauthorized();

                var repo = await repoDataService.GetRepositoryByIdAsync(repositoryId);
                if (repo == null) return Results.NotFound($"Repository {repositoryId} not found.");
                if (repo.UserId != userId) return Results.Forbid();

                repo.LocalPath = localPath;
                await repoDataService.UpdateRepositoryAsync(repo);
                Log.Information("DEV: Patched LocalPath for repository {RepositoryId} to {LocalPath}", repositoryId, localPath);
                return Results.Ok(new { repositoryId, localPath });
            })
            .WithName("DevFixLocalPath");
        }
    }
}
