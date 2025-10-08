# Logging & Telemetry Guide

## Overview

PoRepoLineTracker implements a comprehensive logging and telemetry solution using:

- **Serilog** for structured logging
- **Application Insights** for cloud telemetry
- **Client-to-Server logging** for Blazor WebAssembly diagnostics

## Architecture

```
┌─────────────────────┐
│  Blazor Client      │
│  (Browser)          │
└──────────┬──────────┘
           │ POST /api/log/client
           ▼
┌─────────────────────┐
│  ASP.NET Core API   │
│  + Serilog          │
└──────────┬──────────┘
           │
     ┌─────┴─────┐
     │           │
     ▼           ▼
┌─────────┐  ┌──────────────────┐
│ Console │  │ Application      │
│ (Dev)   │  │ Insights         │
└─────────┘  │ (Azure)          │
┌─────────┐  └──────────────────┘
│  File   │
│ (Dev)   │
└─────────┘
```

## Server-Side Logging

### Configuration

Serilog is configured in `Program.cs` with multiple sinks:

1. **Console Sink** - Always active
2. **File Sink** - Development environment only (`log.txt`)
3. **Application Insights Sink** - When connection string is provided

```csharp
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .WriteTo.Console()
        .WriteTo.File("log.txt") // Dev only
        .WriteTo.ApplicationInsights(...) // When available
        .MinimumLevel.Information();
});
```

### Log Levels

- **Information**: Normal operational events (repository added, analysis complete)
- **Warning**: Unexpected but recoverable events (retry attempts, configuration issues)
- **Error**: Operation failures, exceptions
- **Debug**: Development-time diagnostic information (file sink only in dev)

### Structured Logging Example

```csharp
_logger.LogInformation(
    "Successfully added repository {Owner}/{RepoName} with ID {RepositoryId} in {ElapsedMs}ms",
    request.Owner,
    request.RepoName,
    repository.Id,
    stopwatch.ElapsedMilliseconds);
```

**Benefits:**
- Searchable properties in Application Insights
- Enables rich KQL queries
- Performance tracking built-in

## Client-Side Logging

### ClientLogger Service

Located in `PoRepoLineTracker.Client/Services/ClientLogger.cs`

**Features:**
- Sends logs to server via `/api/log/client` endpoint
- Automatic fallback to console logging
- Telemetry tracking methods

### Usage in Blazor Components

```csharp
@inject ClientLogger Logger

@code {
    protected override async Task OnInitializedAsync()
    {
        await Logger.TrackPageViewAsync("Repositories");

        try
        {
            // Your code
            await Logger.TrackEventAsync("RepositoryLoaded", new Dictionary<string, object>
            {
                { "Count", repositories.Count }
            });
        }
        catch (Exception ex)
        {
            await Logger.LogErrorAsync("Failed to load repositories", ex);
        }
    }
}
```

### Available Methods

- `LogInformationAsync(message, properties)` - Log informational events
- `LogWarningAsync(message, properties)` - Log warnings
- `LogErrorAsync(message, exception, properties)` - Log errors with exceptions
- `TrackEventAsync(eventName, properties)` - Track custom events
- `TrackPageViewAsync(pageName, properties)` - Track page views
- `TrackMetricAsync(metricName, value, properties)` - Track metrics

## Application Insights Integration

### Configuration

Connection string configured via:

1. **Local Development**: Not required (uses file/console sinks)
2. **Azure Deployment**: Automatically set via Bicep template

```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=...;IngestionEndpoint=..."
}
```

### Custom Telemetry

The application automatically tracks:

**Operations:**
- Repository additions (with Owner, RepoName, RepositoryId)
- Repository analysis (with CommitCount, TotalLines, Duration)
- Failed operations (with RetryCount, Exception details)
- Health checks (with Status, component health)

**Performance Metrics:**
- Operation duration (ElapsedMs custom dimension)
- Request/response times
- Dependency calls (Azure Storage, GitHub API)

**Custom Events:**
- Client-side page views
- User interactions
- Business events

### Accessing Telemetry

1. **Azure Portal** → Application Insights → Logs
2. Run KQL queries (see `docs/KQL-Queries.md`)
3. View Live Metrics, Failures, Performance tabs

## Development vs Production

### Development Environment

**Active Sinks:**
- ✅ Console (stdout)
- ✅ File (`log.txt`, daily rolling, 7-day retention)
- ✅ Client logging endpoint (`/api/log/client`)
- ⚠️ Application Insights (if connection string provided)

**Log Level:** Information and above

### Production Environment

**Active Sinks:**
- ✅ Console (for container logs)
- ❌ File (disabled for security/performance)
- ❌ Client logging endpoint (disabled for security)
- ✅ Application Insights (required)

**Log Level:** Warning and above (configured via appsettings.json)

## Telemetry Best Practices

### DO ✅

1. **Use structured logging** with named parameters
   ```csharp
   _logger.LogInformation("Repository {RepoId} analyzed in {Ms}ms", id, elapsed);
   ```

2. **Track performance** for operations > 100ms
   ```csharp
   var sw = Stopwatch.StartNew();
   // ... operation ...
   _logger.LogInformation("Operation completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
   ```

3. **Include context** in error logs
   ```csharp
   _logger.LogError(ex, "Failed to process {Owner}/{Repo}", owner, repo);
   ```

4. **Track business events**
   ```csharp
   await _clientLogger.TrackEventAsync("RepositoryAdded", new { Owner, Repo });
   ```

### DON'T ❌

1. **Don't log sensitive data**
   - ❌ GitHub Personal Access Tokens
   - ❌ Connection strings
   - ❌ User passwords/secrets

2. **Don't log in tight loops**
   - ❌ Per-file logging during analysis
   - ✅ Summary logging after batch operations

3. **Don't use string interpolation for log messages**
   ```csharp
   // ❌ Bad
   _logger.LogInformation($"Processing {repo}");

   // ✅ Good
   _logger.LogInformation("Processing {Repo}", repo);
   ```

## Monitoring & Alerts

### Recommended Alerts

See `docs/KQL-Queries.md` for query details.

1. **Error Rate Alert**
   - Threshold: > 10 errors in 5 minutes
   - Action: Send email/SMS to ops team

2. **Performance Degradation**
   - Threshold: P95 latency > 5000ms
   - Action: Investigate slow operations

3. **Health Check Failures**
   - Threshold: Any failed health check
   - Action: Check Azure Storage and GitHub API connectivity

### Dashboards

Create custom dashboards in Azure Portal:

1. **Operations Dashboard**
   - Repository additions over time
   - Analysis completion rate
   - Error rate

2. **Performance Dashboard**
   - Operation duration percentiles
   - Dependency response times
   - Resource usage

## Troubleshooting

### Logs Not Appearing in Application Insights

1. Check connection string: `echo $env:APPLICATIONINSIGHTS_CONNECTION_STRING`
2. Verify Application Insights resource exists in Azure
3. Check ingestion delay (can be 2-5 minutes)
4. Run query: `traces | where timestamp > ago(1h)`

### Client Logs Not Reaching Server

1. Check browser console for errors
2. Verify `/api/log/client` endpoint is available (Development only)
3. Check network tab for HTTP 200 response
4. Verify ClientLogger is registered in `Program.cs`

### File Log Not Created

1. Check write permissions in API project directory
2. Verify Development environment: `IsDevelopment() == true`
3. Check Serilog configuration in `Program.cs`

## Performance Impact

### Logging Overhead

- **Console**: < 1ms per log entry
- **File**: < 5ms per log entry (async write)
- **Application Insights**: < 10ms per log entry (batched/async)
- **Client-to-Server**: < 50ms (HTTP POST)

### Best Practices

- Use `LogLevel.Information` or higher in production
- Avoid logging in hot paths (tight loops)
- Use async logging (built-in with Serilog)
- Batch client-side logs if high volume

## Additional Resources

- **KQL Queries**: `docs/KQL-Queries.md`
- **Serilog Documentation**: https://serilog.net/
- **Application Insights**: https://docs.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview
- **KQL Reference**: https://docs.microsoft.com/en-us/azure/data-explorer/kusto/query/
