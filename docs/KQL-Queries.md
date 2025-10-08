# Application Insights KQL Queries for PoRepoLineTracker

This document contains essential KQL (Kusto Query Language) queries for monitoring and analyzing PoRepoLineTracker telemetry data in Azure Application Insights.

## Quick Access

Run these queries in Azure Portal > Application Insights > Logs

## Essential Queries

### 1. Error Analysis - Last 24 Hours

```kql
// Find all errors and exceptions in the last 24 hours
// Shows error message, operation, timestamp, and custom properties
traces
| where timestamp > ago(24h)
| where severityLevel >= 3  // 3=Error, 4=Critical
| union (
    exceptions
    | where timestamp > ago(24h)
)
| project
    timestamp,
    operation_Name,
    severityLevel,
    message,
    customDimensions.Owner,
    customDimensions.RepoName,
    customDimensions.RepositoryId,
    customDimensions.Exception,
    cloud_RoleName
| order by timestamp desc
| take 100
```

**Use Case:** Troubleshooting errors, identifying failing operations

**Key Insights:**
- Recent error patterns
- Which repositories are failing
- Error frequency and timing

---

### 2. Repository Usage Analytics

```kql
// Analyze repository operations: adds, deletes, analysis runs
// Groups by operation type and shows counts, avg duration
traces
| where timestamp > ago(7d)
| where message contains "repository" or message contains "Repository"
| extend
    Operation = case(
        message contains "Adding repository", "Add",
        message contains "Successfully added repository", "AddComplete",
        message contains "Deleting repository", "Delete",
        message contains "Analyzing repository", "Analyze",
        message contains "Successfully analyzed", "AnalyzeComplete",
        "Other"
    )
| where Operation != "Other"
| extend
    Owner = tostring(customDimensions.Owner),
    RepoName = tostring(customDimensions.RepoName),
    ElapsedMs = todouble(customDimensions.ElapsedMs),
    RepositoryId = tostring(customDimensions.RepositoryId)
| summarize
    Count = count(),
    AvgDurationMs = avg(ElapsedMs),
    MaxDurationMs = max(ElapsedMs),
    UniqueRepos = dcount(RepositoryId)
    by Operation, bin(timestamp, 1h)
| order by timestamp desc, Count desc
```

**Use Case:** Understanding usage patterns, capacity planning

**Key Insights:**
- Most common operations
- Peak usage times
- Performance trends over time
- Number of unique repositories being processed

---

### 3. Performance Monitoring - Slow Operations

```kql
// Identify slow operations (>1000ms) that may need optimization
// Includes percentile analysis and operation breakdown
traces
| where timestamp > ago(24h)
| where customDimensions.ElapsedMs != ""
| extend
    ElapsedMs = todouble(customDimensions.ElapsedMs),
    Operation = operation_Name,
    Owner = tostring(customDimensions.Owner),
    RepoName = tostring(customDimensions.RepoName)
| where ElapsedMs > 1000  // Operations taking more than 1 second
| summarize
    Count = count(),
    P50 = percentile(ElapsedMs, 50),
    P95 = percentile(ElapsedMs, 95),
    P99 = percentile(ElapsedMs, 99),
    Max = max(ElapsedMs),
    AvgDuration = avg(ElapsedMs)
    by Operation
| order by AvgDuration desc
```

**Use Case:** Performance optimization, identifying bottlenecks

**Key Insights:**
- Slowest operations
- Performance percentiles (P50, P95, P99)
- Operations needing optimization

---

## Additional Useful Queries

### Client-Side Error Tracking

```kql
// Track client-side errors sent from Blazor WebAssembly
traces
| where timestamp > ago(24h)
| where message startswith "[CLIENT]"
| where severityLevel >= 3  // Errors and above
| project
    timestamp,
    message,
    severityLevel,
    customDimensions
| order by timestamp desc
| take 50
```

### Health Check Monitoring

```kql
// Monitor application health checks
traces
| where timestamp > ago(1h)
| where operation_Name == "HealthCheck"
| extend
    Status = tostring(customDimensions.Status),
    AzureTableStorage = tostring(customDimensions.AzureTableStorage),
    GitHubAPI = tostring(customDimensions.GitHubAPI)
| project
    timestamp,
    Status,
    AzureTableStorage,
    GitHubAPI
| order by timestamp desc
```

### Daily Repository Analysis Summary

```kql
// Daily summary of repository analysis activity
traces
| where timestamp > ago(30d)
| where message contains "Successfully analyzed"
| extend
    Owner = tostring(customDimensions.Owner),
    RepoName = tostring(customDimensions.RepoName),
    CommitCount = toint(customDimensions.CommitCount),
    TotalLines = toint(customDimensions.TotalLines)
| summarize
    RepositoriesAnalyzed = count(),
    TotalCommits = sum(CommitCount),
    TotalLinesProcessed = sum(TotalLines)
    by bin(timestamp, 1d)
| order by timestamp desc
```

### Failed Operation Dead Letter Queue

```kql
// Monitor failed operations and retry attempts
traces
| where timestamp > ago(7d)
| where message contains "Failed operation" or message contains "Retry"
| extend
    RepositoryId = tostring(customDimensions.RepositoryId),
    RetryCount = toint(customDimensions.RetryCount),
    Operation = tostring(customDimensions.Operation)
| summarize
    FailureCount = count(),
    AvgRetries = avg(RetryCount),
    MaxRetries = max(RetryCount)
    by RepositoryId, Operation
| order by FailureCount desc
```

## Custom Metrics

### Track Average Lines of Code Over Time

```kql
customMetrics
| where timestamp > ago(30d)
| where name == "LinesOfCode"
| summarize
    AvgLines = avg(value),
    MaxLines = max(value),
    MinLines = min(value)
    by bin(timestamp, 1d)
| render timechart
```

## Alerts (Recommended)

Configure these alerts in Azure Monitor:

1. **High Error Rate**
   - Condition: > 10 errors in 5 minutes
   - Query: `traces | where severityLevel >= 3 | summarize count()`

2. **Slow Performance**
   - Condition: P95 latency > 5000ms
   - Query: Based on Performance Monitoring query above

3. **Failed Health Checks**
   - Condition: Health check failures
   - Query: `traces | where operation_Name == "HealthCheck" and customDimensions.Status != "Healthy"`

## Usage Tips

1. **Time Ranges**: Adjust `ago(24h)`, `ago(7d)`, etc. based on your needs
2. **Filtering**: Add `where cloud_RoleName == "PoRepoLineTracker"` to filter by application
3. **Visualization**: Add `| render timechart` to create time-series visualizations
4. **Export**: Use "Export > Export to Excel" or "Pin to dashboard" for regular monitoring

## Custom Dimensions Reference

Common custom dimensions logged by PoRepoLineTracker:

- `Owner`: Repository owner (user or organization)
- `RepoName`: Repository name
- `RepositoryId`: Unique repository identifier (GUID)
- `ElapsedMs`: Operation duration in milliseconds
- `CommitCount`: Number of commits processed
- `TotalLines`: Total lines of code
- `RetryCount`: Number of retry attempts for failed operations
- `Exception`: Exception details (when errors occur)

## Performance Baseline

Expected performance metrics (adjust alerts based on these):

- **Add Repository**: < 500ms (P95)
- **Analyze Repository**: < 30000ms (P95) - varies by repository size
- **Get Line History**: < 1000ms (P95)
- **Health Check**: < 500ms (P95)
