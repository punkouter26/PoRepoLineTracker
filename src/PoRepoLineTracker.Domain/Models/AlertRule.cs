namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// SmartAlert: Represents a user-defined alert rule that triggers notifications
/// when repository metrics cross specified thresholds.
/// </summary>
public class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user who owns this alert rule.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Optional: specific repository this rule applies to.
    /// If null, the rule applies to all repositories.
    /// </summary>
    public Guid? RepositoryId { get; set; }

    /// <summary>
    /// The metric being monitored.
    /// </summary>
    public AlertMetric Metric { get; set; }

    /// <summary>
    /// The comparison operator for the threshold.
    /// </summary>
    public AlertOperator Operator { get; set; }

    /// <summary>
    /// The threshold value to compare against.
    /// </summary>
    public double ThresholdValue { get; set; }

    /// <summary>
    /// Human-readable name for this rule.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this rule is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the rule was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the rule was last triggered.
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }
}

// AlertMetric and AlertOperator now live in the Shared (leaf) assembly — still under the
// PoRepoLineTracker.Domain.Models namespace — see Shared/Models/SharedContractEnums.cs (Rule 2.2).

/// <summary>
/// Represents a triggered alert instance.
/// </summary>
public class AlertTrigger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertRuleId { get; set; }
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double ThresholdValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
