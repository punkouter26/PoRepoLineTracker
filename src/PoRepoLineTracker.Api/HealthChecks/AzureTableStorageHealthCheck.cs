using Azure.Data.Tables;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoRepoLineTracker.Api.HealthChecks;

public class AzureTableStorageHealthCheck : IHealthCheck
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly string _healthCheckTableName;
    private readonly ILogger<AzureTableStorageHealthCheck> _logger;

    public AzureTableStorageHealthCheck(
        TableServiceClient tableServiceClient,
        IConfiguration configuration,
        ILogger<AzureTableStorageHealthCheck> logger)
    {
        _tableServiceClient = tableServiceClient;
        _healthCheckTableName = configuration["AzureTableStorage:RepositoryTableName"] ?? "PoRepoLineTrackerRepositories";
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check a known table with the same data-plane permissions used by the app.
            var tableClient = _tableServiceClient.GetTableClient(_healthCheckTableName);

            await foreach (var _ in tableClient.QueryAsync<TableEntity>(maxPerPage: 1, cancellationToken: cancellationToken))
            {
                break;
            }

            return HealthCheckResult.Healthy($"Azure Table Storage table '{_healthCheckTableName}' is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Azure Table Storage health check failed for table {TableName}",
                _healthCheckTableName);

            return HealthCheckResult.Unhealthy(
                $"Azure Table Storage table '{_healthCheckTableName}' is not accessible",
                exception: ex);
        }
    }
}
