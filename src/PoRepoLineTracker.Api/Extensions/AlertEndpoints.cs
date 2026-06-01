using MediatR;
using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Shared.Models.Dtos;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

/// <summary>
/// SmartAlert: API endpoints for managing alert rules and viewing triggered alerts.
/// </summary>
internal static class AlertEndpoints
{
    internal static void MapAlertEndpoints(this WebApplication app)
    {
        // ── Alert Rules ──────────────────────────────────────────────────────────

        app.MapGet("/api/alerts/rules", async (HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var rules = await alertService.GetRulesForUserAsync(userId.Value);
                var dtos = rules.Select(r => new AlertRuleDto
                {
                    Id = r.Id,
                    RepositoryId = r.RepositoryId,
                    Metric = r.Metric,
                    Operator = r.Operator,
                    ThresholdValue = r.ThresholdValue,
                    Name = r.Name,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    LastTriggeredAt = r.LastTriggeredAt
                });
                return Results.Ok(dtos);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving alert rules for user {UserId}", userId);
                return Results.Problem($"Error retrieving alert rules: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("GetAlertRules");

        app.MapPost("/api/alerts/rules", async (CreateAlertRuleRequest request, HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var rule = new AlertRule
                {
                    UserId = userId.Value,
                    RepositoryId = request.RepositoryId,
                    Metric = request.Metric,
                    Operator = request.Operator,
                    ThresholdValue = request.ThresholdValue,
                    Name = request.Name
                };
                var created = await alertService.CreateRuleAsync(rule);
                return Results.Created($"/api/alerts/rules/{created.Id}", new AlertRuleDto
                {
                    Id = created.Id,
                    RepositoryId = created.RepositoryId,
                    Metric = created.Metric,
                    Operator = created.Operator,
                    ThresholdValue = created.ThresholdValue,
                    Name = created.Name,
                    IsActive = created.IsActive,
                    CreatedAt = created.CreatedAt
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating alert rule for user {UserId}", userId);
                return Results.Problem($"Error creating alert rule: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("CreateAlertRule");

        app.MapDelete("/api/alerts/rules/{ruleId}", async (Guid ruleId, HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                await alertService.DeleteRuleAsync(ruleId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting alert rule {RuleId}", ruleId);
                return Results.Problem($"Error deleting alert rule: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("DeleteAlertRule");

        app.MapPatch("/api/alerts/rules/{ruleId}/toggle", async (Guid ruleId, HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var rule = await alertService.GetRuleByIdAsync(ruleId);
                if (rule == null) return Results.NotFound();
                rule.IsActive = !rule.IsActive;
                await alertService.UpdateRuleAsync(rule);
                return Results.Ok(new { rule.IsActive });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error toggling alert rule {RuleId}", ruleId);
                return Results.Problem($"Error toggling alert rule: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("ToggleAlertRule");

        // ── Alert Triggers ───────────────────────────────────────────────────────

        app.MapGet("/api/alerts/triggers", async (HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var triggers = await alertService.GetTriggersForUserAsync(userId.Value);
                var dtos = triggers.Select(t => new AlertTriggerDto
                {
                    Id = t.Id,
                    AlertRuleId = t.AlertRuleId,
                    RepositoryId = t.RepositoryId,
                    RepositoryName = t.RepositoryName,
                    ActualValue = t.ActualValue,
                    ThresholdValue = t.ThresholdValue,
                    Message = t.Message,
                    TriggeredAt = t.TriggeredAt,
                    IsRead = t.IsRead
                });
                return Results.Ok(dtos);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving alert triggers for user {UserId}", userId);
                return Results.Problem($"Error retrieving alert triggers: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("GetAlertTriggers");

        app.MapGet("/api/alerts/triggers/unread-count", async (HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var count = await alertService.GetUnreadTriggerCountAsync(userId.Value);
                return Results.Ok(count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving unread alert count for user {UserId}", userId);
                return Results.Problem($"Error retrieving unread count: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("GetUnreadAlertCount");

        app.MapPost("/api/alerts/triggers/{triggerId}/read", async (Guid triggerId, HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                await alertService.MarkTriggerAsReadAsync(triggerId);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error marking alert trigger {TriggerId} as read", triggerId);
                return Results.Problem($"Error marking trigger as read: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("MarkAlertTriggerAsRead");

        app.MapPost("/api/alerts/triggers/read-all", async (HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                await alertService.MarkAllTriggersAsReadAsync(userId.Value);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error marking all alert triggers as read for user {UserId}", userId);
                return Results.Problem($"Error marking all as read: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("MarkAllAlertTriggersAsRead");

        // ── Evaluate (called after analysis completes) ───────────────────────────

        app.MapPost("/api/alerts/evaluate/{repositoryId}", async (Guid repositoryId, HttpContext ctx, IAlertService alertService) =>
        {
            var userId = GetUserId(ctx);
            if (userId == null) return Results.Unauthorized();

            try
            {
                var triggers = await alertService.EvaluateRulesAsync(userId.Value, repositoryId);
                return Results.Ok(triggers.Select(t => new AlertTriggerDto
                {
                    Id = t.Id,
                    AlertRuleId = t.AlertRuleId,
                    RepositoryId = t.RepositoryId,
                    RepositoryName = t.RepositoryName,
                    ActualValue = t.ActualValue,
                    ThresholdValue = t.ThresholdValue,
                    Message = t.Message,
                    TriggeredAt = t.TriggeredAt,
                    IsRead = t.IsRead
                }));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error evaluating alert rules for repository {RepositoryId}", repositoryId);
                return Results.Problem($"Error evaluating alerts: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .WithName("EvaluateAlerts");
    }

    private static Guid? GetUserId(HttpContext ctx)
    {
        var userIdClaim = ctx.User.FindFirst("UserId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return null;
        return userId;
    }
}
