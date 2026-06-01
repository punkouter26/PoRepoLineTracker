using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// DTO for creating/updating an alert rule.
/// </summary>
public class AlertRuleDto
{
    public Guid Id { get; set; }
    public Guid? RepositoryId { get; set; }
    public AlertMetric Metric { get; set; }
    public AlertOperator Operator { get; set; }
    public double ThresholdValue { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// Optional repository display name for UI.
    /// </summary>
    public string? RepositoryName { get; set; }
}

/// <summary>
/// DTO for a triggered alert notification.
/// </summary>
public class AlertTriggerDto
{
    public Guid Id { get; set; }
    public Guid AlertRuleId { get; set; }
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double ThresholdValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>
/// Request body for creating a new alert rule.
/// </summary>
public class CreateAlertRuleRequest
{
    public Guid? RepositoryId { get; set; }
    public AlertMetric Metric { get; set; }
    public AlertOperator Operator { get; set; }
    public double ThresholdValue { get; set; }
    public string Name { get; set; } = string.Empty;
}
