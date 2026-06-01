using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// SmartAlert: Service for managing alert rules and evaluating triggered alerts.
/// </summary>
public interface IAlertService
{
    // ── Rule Management ──────────────────────────────────────────────────────
    Task<AlertRule> CreateRuleAsync(AlertRule rule);
    Task<AlertRule?> GetRuleByIdAsync(Guid id);
    Task<IEnumerable<AlertRule>> GetRulesForUserAsync(Guid userId);
    Task<IEnumerable<AlertRule>> GetActiveRulesForUserAsync(Guid userId);
    Task UpdateRuleAsync(AlertRule rule);
    Task DeleteRuleAsync(Guid id);

    // ── Trigger Management ───────────────────────────────────────────────────
    Task<IEnumerable<AlertTrigger>> GetTriggersForUserAsync(Guid userId);
    Task<IEnumerable<AlertTrigger>> GetUnreadTriggersForUserAsync(Guid userId);
    Task<int> GetUnreadTriggerCountAsync(Guid userId);
    Task MarkTriggerAsReadAsync(Guid triggerId);
    Task MarkAllTriggersAsReadAsync(Guid userId);

    // ── Evaluation ───────────────────────────────────────────────────────────
    /// <summary>
    /// Evaluates all active alert rules for a user against current repository metrics.
    /// Returns any newly triggered alerts.
    /// </summary>
    Task<IEnumerable<AlertTrigger>> EvaluateRulesAsync(Guid userId, Guid repositoryId);
}
