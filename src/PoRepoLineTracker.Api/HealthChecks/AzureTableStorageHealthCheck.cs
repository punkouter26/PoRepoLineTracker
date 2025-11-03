using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoRepoLineTracker.Api.HealthChecks;

public class AzureTableStorageHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureTableStorageHealthCheck> _logger;

    public AzureTableStorageHealthCheck(
        IConfiguration configuration,
        ILogger<AzureTableStorageHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = _configuration["AzureTableStorage:ConnectionString"];

            if (string.IsNullOrEmpty(connectionString))
            {
                return HealthCheckResult.Unhealthy("Azure Table Storage connection string is not configured");
            }

            // Simple check - if we can instantiate a client, the connection string is valid
            var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connectionString);

            // Try to get account info - this validates the connection
            await tableServiceClient.GetPropertiesAsync(cancellationToken);

            return HealthCheckResult.Healthy("Azure Table Storage is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Table Storage health check failed");
            return HealthCheckResult.Unhealthy(
                "Azure Table Storage is not accessible",
                exception: ex);
        }
    }
}
