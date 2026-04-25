using MediatR;
using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Application.Features.Repositories.Queries;
using System.Net;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class AiDetectionEndpoints
{
    internal static void MapAiDetectionEndpoints(this WebApplication app)
    {
        // Get AI detection statistics by user for a repository
        app.MapGet("/api/repositories/{repositoryId}/ai-stats/{days}", async (Guid repositoryId, int days, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read ai-stats for repo {RepositoryId} owned by {OwnerId}", 
                    userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            try
            {
                var aiStats = await mediator.Send(new GetAiStatsByUserQuery(repositoryId, days));
                return Results.Ok(aiStats);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving AI stats for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving AI stats: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetAiStatsByUser");

        // Get daily AI detection statistics for a repository
        app.MapGet("/api/repositories/{repositoryId}/ai-daily/{days}", async (Guid repositoryId, int days, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read ai-daily for repo {RepositoryId} owned by {OwnerId}", 
                    userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            try
            {
                var commits = await repoDataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId);
                var cutoffDate = DateTime.UtcNow.AddDays(-days);
                
                var dailyStats = commits
                    .Where(c => c.CommitDate >= cutoffDate)
                    .GroupBy(c => c.CommitDate.Date)
                    .Select(g => new DailyAiDetectionDto
                    {
                        Date = g.Key,
                        CommitCount = g.Count(),
                        AverageAiPercentage = g.Any(c => c.AiPercentage > 0) 
                            ? Math.Round(g.Where(c => c.AiPercentage > 0).Average(c => c.AiPercentage), 2)
                            : 0,
                        AuthorBreakdown = g
                            .GroupBy(c => string.IsNullOrEmpty(c.AuthorName) ? c.AuthorEmail : c.AuthorName)
                            .ToDictionary(
                                authorGroup => authorGroup.Key,
                                authorGroup => Math.Round(
                                    authorGroup.Where(c => c.AiPercentage > 0).Any() 
                                        ? authorGroup.Where(c => c.AiPercentage > 0).Average(c => c.AiPercentage)
                                        : 0, 2))
                    })
                    .OrderBy(d => d.Date)
                    .ToList();

                return Results.Ok(dailyStats);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving daily AI stats for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving daily AI stats: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetDailyAiStats");

        // Get top contributors by lines of code
        app.MapGet("/api/repositories/{repositoryId}/contributors/{days}", async (Guid repositoryId, int days, int topN, HttpContext ctx, IMediator mediator, IRepositoryDataService repoDataService) =>
        {
            var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var existing = await repoDataService.GetRepositoryByIdAsync(repositoryId);
            if (existing == null) return Results.NotFound($"Repository {repositoryId} not found.");
            if (existing.UserId != userId)
            {
                Log.Warning("IDOR attempt: user {UserId} tried to read contributors for repo {RepositoryId} owned by {OwnerId}", 
                    userId, repositoryId, existing.UserId);
                return Results.Forbid();
            }

            try
            {
                var contributors = await mediator.Send(new GetContributorStatsQuery(repositoryId, days, topN > 0 ? topN : 10));
                return Results.Ok(contributors);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving contributor stats for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error retrieving contributor stats: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("GetContributorStats");
    }
}
