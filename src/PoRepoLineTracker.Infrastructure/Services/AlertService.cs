using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using System.Text.Json;

namespace PoRepoLineTracker.Infrastructure.Services;

/// <summary>
/// SmartAlert: Azure Table Storage implementation of IAlertService.
/// Stores alert rules and triggered alerts in separate tables.
/// </summary>
public class AlertService : IAlertService
{
    private readonly TableClient _rulesTableClient;
    private readonly TableClient _triggersTableClient;
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<AlertService> _logger;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _tablesInitialized;

    public AlertService(
        TableServiceClient tableServiceClient,
        IRepositoryDataService repositoryDataService,
        IConfiguration configuration,
        ILogger<AlertService> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;

        var rulesTableName = configuration["AzureTableStorage:AlertRulesTableName"] ?? "PoRepoLineTrackerAlertRules";
        var triggersTableName = configuration["AzureTableStorage:AlertTriggersTableName"] ?? "PoRepoLineTrackerAlertTriggers";

        _rulesTableClient = tableServiceClient.GetTableClient(rulesTableName);
        _triggersTableClient = tableServiceClient.GetTableClient(triggersTableName);
    }

    private async Task EnsureTablesExistAsync()
    {
        if (_tablesInitialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_tablesInitialized) return;
            await _rulesTableClient.CreateIfNotExistsAsync();
            await _triggersTableClient.CreateIfNotExistsAsync();
            _tablesInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<AlertRule> CreateRuleAsync(AlertRule rule)
    {
        await EnsureTablesExistAsync();
        var entity = AlertRuleEntity.FromDomainModel(rule);
        await _rulesTableClient.AddEntityAsync(entity);
        return rule;
    }

    public async Task<AlertRule?> GetRuleByIdAsync(Guid id)
    {
        await EnsureTablesExistAsync();
        try
        {
            // Search across all partitions (UserId) using the RowKey
            var filter = $"RowKey eq '{id}'";
            await foreach (var entity in _rulesTableClient.QueryAsync<AlertRuleEntity>(filter))
            {
                return entity.ToDomainModel();
            }
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IEnumerable<AlertRule>> GetRulesForUserAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        var results = new List<AlertRule>();
        await foreach (var entity in _rulesTableClient.QueryAsync<AlertRuleEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(entity.ToDomainModel());
        }
        return results;
    }

    public async Task<IEnumerable<AlertRule>> GetActiveRulesForUserAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        var results = new List<AlertRule>();
        await foreach (var entity in _rulesTableClient.QueryAsync<AlertRuleEntity>(
            filter: $"PartitionKey eq '{userId}' and IsActive eq true"))
        {
            results.Add(entity.ToDomainModel());
        }
        return results;
    }

    public async Task UpdateRuleAsync(AlertRule rule)
    {
        await EnsureTablesExistAsync();
        var entity = AlertRuleEntity.FromDomainModel(rule);
        await _rulesTableClient.UpsertEntityAsync(entity);
    }

    public async Task DeleteRuleAsync(Guid id)
    {
        await EnsureTablesExistAsync();
        var rule = await GetRuleByIdAsync(id);
        if (rule != null)
        {
            await _rulesTableClient.DeleteEntityAsync(rule.UserId.ToString(), id.ToString());
        }
    }

    public async Task<IEnumerable<AlertTrigger>> GetTriggersForUserAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        var results = new List<AlertTrigger>();
        await foreach (var entity in _triggersTableClient.QueryAsync<AlertTriggerEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(entity.ToDomainModel());
        }
        return results.OrderByDescending(t => t.TriggeredAt);
    }

    public async Task<IEnumerable<AlertTrigger>> GetUnreadTriggersForUserAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        var results = new List<AlertTrigger>();
        await foreach (var entity in _triggersTableClient.QueryAsync<AlertTriggerEntity>(
            filter: $"PartitionKey eq '{userId}' and IsRead eq false"))
        {
            results.Add(entity.ToDomainModel());
        }
        return results.OrderByDescending(t => t.TriggeredAt);
    }

    public async Task<int> GetUnreadTriggerCountAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        var count = 0;
        await foreach (var _ in _triggersTableClient.QueryAsync<AlertTriggerEntity>(
            filter: $"PartitionKey eq '{userId}' and IsRead eq false",
            select: ["PartitionKey"]))
        {
            count++;
        }
        return count;
    }

    public async Task MarkTriggerAsReadAsync(Guid triggerId)
    {
        await EnsureTablesExistAsync();
        // Find the trigger across all user partitions
        var filter = $"RowKey eq '{triggerId}'";
        await foreach (var entity in _triggersTableClient.QueryAsync<AlertTriggerEntity>(filter))
        {
            entity.IsRead = true;
            await _triggersTableClient.UpsertEntityAsync(entity);
            return;
        }
    }

    public async Task MarkAllTriggersAsReadAsync(Guid userId)
    {
        await EnsureTablesExistAsync();
        await foreach (var entity in _triggersTableClient.QueryAsync<AlertTriggerEntity>(
            filter: $"PartitionKey eq '{userId}' and IsRead eq false"))
        {
            entity.IsRead = true;
            await _triggersTableClient.UpsertEntityAsync(entity);
        }
    }

    public async Task<IEnumerable<AlertTrigger>> EvaluateRulesAsync(Guid userId, Guid repositoryId)
    {
        await EnsureTablesExistAsync();
        var activeRules = await GetActiveRulesForUserAsync(userId);
        var triggeredAlerts = new List<AlertTrigger>();

        var repo = await _repositoryDataService.GetRepositoryByIdAsync(repositoryId);
        if (repo == null) return triggeredAlerts;

        var commits = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId);
        var commitList = commits.OrderByDescending(c => c.CommitDate).ToList();

        foreach (var rule in activeRules.Where(r => r.RepositoryId == null || r.RepositoryId == repositoryId))
        {
            var metricValue = EvaluateMetric(rule.Metric, commitList);
            if (metricValue == null) continue;

            var isTriggered = rule.Operator switch
            {
                AlertOperator.GreaterThan => metricValue.Value > rule.ThresholdValue,
                AlertOperator.LessThan => metricValue.Value < rule.ThresholdValue,
                AlertOperator.GreaterThanOrEqual => metricValue.Value >= rule.ThresholdValue,
                AlertOperator.LessThanOrEqual => metricValue.Value <= rule.ThresholdValue,
                _ => false
            };

            if (isTriggered)
            {
                // Don't re-trigger within 1 hour of the last trigger
                if (rule.LastTriggeredAt.HasValue && (DateTime.UtcNow - rule.LastTriggeredAt.Value) < TimeSpan.FromHours(1))
                    continue;

                var trigger = new AlertTrigger
                {
                    AlertRuleId = rule.Id,
                    RepositoryId = repositoryId,
                    RepositoryName = $"{repo.Owner}/{repo.Name}",
                    ActualValue = metricValue.Value,
                    ThresholdValue = rule.ThresholdValue,
                    Message = FormatTriggerMessage(rule, metricValue.Value, $"{repo.Owner}/{repo.Name}"),
                    TriggeredAt = DateTime.UtcNow,
                    IsRead = false
                };

                var triggerEntity = AlertTriggerEntity.FromDomainModel(trigger, userId);
                await _triggersTableClient.AddEntityAsync(triggerEntity);

                rule.LastTriggeredAt = DateTime.UtcNow;
                await UpdateRuleAsync(rule);

                triggeredAlerts.Add(trigger);
            }
        }

        return triggeredAlerts;
    }

    private static double? EvaluateMetric(AlertMetric metric, List<CommitLineCount> commits)
    {
        if (!commits.Any()) return null;

        return metric switch
        {
            AlertMetric.AiPercentage => commits.Any(c => c.AiPercentage > 0)
                ? commits.Where(c => c.AiPercentage > 0).Average(c => c.AiPercentage)
                : 0,
            AlertMetric.TotalLines => commits.Max(c => c.TotalLines),
            AlertMetric.WeeklyCommitCount => commits
                .Count(c => c.CommitDate >= DateTime.UtcNow.AddDays(-7)),
            AlertMetric.WeeklyLineChange => CalculateWeeklyLineChange(commits),
            _ => null
        };
    }

    private static double CalculateWeeklyLineChange(List<CommitLineCount> commits)
    {
        var thisWeek = commits.Where(c => c.CommitDate >= DateTime.UtcNow.AddDays(-7)).Sum(c => c.LinesAdded);
        var lastWeek = commits.Where(c => c.CommitDate >= DateTime.UtcNow.AddDays(-14) && c.CommitDate < DateTime.UtcNow.AddDays(-7)).Sum(c => c.LinesAdded);
        if (lastWeek == 0) return thisWeek > 0 ? 100.0 : 0.0;
        return ((double)(thisWeek - lastWeek) / lastWeek) * 100;
    }

    private static string FormatTriggerMessage(AlertRule rule, double actualValue, string repoName)
    {
        var opStr = rule.Operator switch
        {
            AlertOperator.GreaterThan => ">",
            AlertOperator.LessThan => "<",
            AlertOperator.GreaterThanOrEqual => "≥",
            AlertOperator.LessThanOrEqual => "≤",
            _ => "?"
        };
        var metricStr = rule.Metric switch
        {
            AlertMetric.AiPercentage => "AI %",
            AlertMetric.WeeklyLineChange => "Weekly line change %",
            AlertMetric.TotalLines => "Total lines",
            AlertMetric.WeeklyCommitCount => "Weekly commits",
            _ => rule.Metric.ToString()
        };
        return $"{repoName}: {metricStr} is {actualValue:F1} ({opStr} {rule.ThresholdValue:F1})";
    }
}

// ── Azure Table Entities ──────────────────────────────────────────────────────

internal class AlertRuleEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string RepositoryId { get; set; } = string.Empty; // empty = all repos
    public int Metric { get; set; }
    public int Operator { get; set; }
    public double ThresholdValue { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string LastTriggeredAt { get; set; } = string.Empty; // ISO 8601 or empty

    public AlertRule ToDomainModel() => new()
    {
        Id = Id,
        UserId = UserId,
        RepositoryId = string.IsNullOrEmpty(RepositoryId) ? null : Guid.Parse(RepositoryId),
        Metric = (AlertMetric)Metric,
        Operator = (AlertOperator)Operator,
        ThresholdValue = ThresholdValue,
        Name = Name,
        IsActive = IsActive,
        CreatedAt = CreatedAt,
        LastTriggeredAt = string.IsNullOrEmpty(LastTriggeredAt) ? null : DateTime.Parse(LastTriggeredAt)
    };

    public static AlertRuleEntity FromDomainModel(AlertRule model) => new()
    {
        PartitionKey = model.UserId.ToString(),
        RowKey = model.Id.ToString(),
        Id = model.Id,
        UserId = model.UserId,
        RepositoryId = model.RepositoryId?.ToString() ?? string.Empty,
        Metric = (int)model.Metric,
        Operator = (int)model.Operator,
        ThresholdValue = model.ThresholdValue,
        Name = model.Name,
        IsActive = model.IsActive,
        CreatedAt = model.CreatedAt,
        LastTriggeredAt = model.LastTriggeredAt?.ToString("O") ?? string.Empty
    };
}

internal class AlertTriggerEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public Guid AlertRuleId { get; set; }
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double ThresholdValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
    public bool IsRead { get; set; }

    public AlertTrigger ToDomainModel() => new()
    {
        Id = Id,
        AlertRuleId = AlertRuleId,
        RepositoryId = RepositoryId,
        RepositoryName = RepositoryName,
        ActualValue = ActualValue,
        ThresholdValue = ThresholdValue,
        Message = Message,
        TriggeredAt = TriggeredAt,
        IsRead = IsRead
    };

    public static AlertTriggerEntity FromDomainModel(AlertTrigger model, Guid userId) => new()
    {
        PartitionKey = userId.ToString(),
        RowKey = model.Id.ToString(),
        Id = model.Id,
        AlertRuleId = model.AlertRuleId,
        RepositoryId = model.RepositoryId,
        RepositoryName = model.RepositoryName,
        ActualValue = model.ActualValue,
        ThresholdValue = model.ThresholdValue,
        Message = model.Message,
        TriggeredAt = model.TriggeredAt,
        IsRead = model.IsRead
    };
}
