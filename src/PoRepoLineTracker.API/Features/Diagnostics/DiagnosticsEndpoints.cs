using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.ComponentModel.DataAnnotations;
using Serilog;

namespace PoRepoLineTracker.API.Features.Diagnostics;

internal static class DiagnosticsEndpoints
{
    internal static void MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // /health is served by the registered IHealthCheck pipeline via app.MapHealthChecks("/health")
        // in Program.cs — no custom implementation needed here.

        // Rule 3.2 — /diag returns configuration with every secret VALUE masked. The Blazor
        // client also has a /diag page; this negotiates on Accept, so a browser gets the page
        // shell while an API caller (Accept: application/json, e.g. the post-deploy smoke test)
        // gets this JSON.
        endpoints.MapGet("/diag", (HttpContext context, IConfiguration configuration, IWebHostEnvironment env) =>
            {
                var accept = context.Request.Headers.Accept.ToString();
                if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var shell = Path.Combine(env.WebRootPath ?? string.Empty, "index.html");
                    if (File.Exists(shell)) return Results.File(shell, "text/html");
                }

                return Results.Ok(BuildMaskedConfiguration(configuration, env));
            })
            .WithTags("Diagnostics")
            .WithName("Diag")
            .WithSummary("Masked configuration snapshot — secret values are never returned in full")
            .RequireAuthorization();

        var diagnostics = endpoints.MapGroup("/api/diagnostics")
            .WithTags("Diagnostics")
            .RequireAuthorization();

        diagnostics.MapGet("/", async (IConfiguration configuration, IWebHostEnvironment env, HealthCheckService healthChecks) =>
        {
            return await MapDiagnosticsData(configuration, env, healthChecks);
        })
        .WithName("ApiDiagnostics");
    }

    /// <summary>
    /// Every configuration key <see cref="ConfigKeys"/> knows about, with secret values masked.
    /// Non-secret keys (table names, endpoints, environment) are returned verbatim because the
    /// point of /diag is confirming a deploy read the settings it was supposed to.
    /// </summary>
    internal static object BuildMaskedConfiguration(IConfiguration configuration, IWebHostEnvironment env)
    {
        // Secrets are reported as Configured yes/no plus a masked hint, never a usable value.
        var secrets = new[]
        {
            ConfigKeys.GitHub.ClientId,
            ConfigKeys.GitHub.ClientSecret,
            ConfigKeys.GitHub.Pat,
            ConfigKeys.Microsoft.ClientId,
            ConfigKeys.Microsoft.ClientSecret,
            ConfigKeys.AzureTableStorage.ConnectionString,
            ConfigKeys.AzureTableStorage.TablesConnectionString,
            ConfigKeys.Telemetry.AppInsightsConnectionString,
        };

        var nonSecrets = new[]
        {
            ConfigKeys.KeyVault.Uri,
            ConfigKeys.AzureTableStorage.ServiceUrl,
            ConfigKeys.AzureTableStorage.RepositoryTableName,
            ConfigKeys.AzureTableStorage.CommitLineCountTableName,
            ConfigKeys.AzureTableStorage.TopFilesTableName,
            ConfigKeys.AzureTableStorage.UserTableName,
            ConfigKeys.AzureTableStorage.UserPreferencesTableName,
            ConfigKeys.Telemetry.OtlpEndpoint,
            ConfigKeys.GitHub.CallbackPath,
        };

        return new
        {
            Environment = env.EnvironmentName,
            Timestamp = DateTime.UtcNow,
            Secrets = secrets.ToDictionary(
                key => key,
                key => new { Configured = !string.IsNullOrEmpty(configuration[key]), Value = Mask(configuration[key]) }),
            Configuration = nonSecrets.ToDictionary(
                key => key,
                key => configuration[key] ?? string.Empty)
        };
    }

    /// <summary>
    /// Masks a secret to "****" plus its last four characters — enough to tell two credentials
    /// apart in a smoke test without disclosing either. Values of four characters or fewer are
    /// masked completely, so a short secret can never be reconstructed from the hint.
    /// </summary>
    internal static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= 4 ? "****" : $"****{value[^4..]}";
    }

    private static async Task<IResult> MapDiagnosticsData(IConfiguration configuration, IWebHostEnvironment env, HealthCheckService healthChecks)
    {
        HealthReport report;
        try
        {
            report = await healthChecks.CheckHealthAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Health check execution failed during diagnostics request");
            report = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["health_check_error"] = new HealthReportEntry(
                        HealthStatus.Unhealthy, ex.Message, TimeSpan.Zero, ex, null)
                },
                HealthStatus.Unhealthy,
                TimeSpan.Zero);
        }

        int configuredCount = 0;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.KeyVault.Uri])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.AzureTableStorage.ServiceUrl]) || !string.IsNullOrEmpty(configuration[ConfigKeys.AzureTableStorage.ConnectionString])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.Telemetry.AppInsightsConnectionString])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.GitHub.ClientId])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.GitHub.Pat])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration[ConfigKeys.Telemetry.OtlpEndpoint])) configuredCount++;

        var externalConnections = new
        {
            Azure = new[]
            {
                new { Name = "Azure Key Vault", Type = "Secret Storage", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.KeyVault.Uri]) ? "Configured" : "Not configured", Purpose = "Securely stores secrets" },
                new { Name = "Azure Table Storage", Type = "Data Storage", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.AzureTableStorage.ServiceUrl]) || !string.IsNullOrEmpty(configuration[ConfigKeys.AzureTableStorage.ConnectionString]) ? "Configured" : "Not configured", Purpose = "Stores repository analysis data" },
                new { Name = "Azure Application Insights", Type = "Telemetry", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.Telemetry.AppInsightsConnectionString]) ? "Configured" : "Not configured", Purpose = "Performance monitoring" }
            },
            GitHub = new[]
            {
                new { Name = "GitHub OAuth", Type = "Authentication", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.GitHub.ClientId]) ? "Configured" : "Not configured", Purpose = "User authentication" },
                new { Name = "GitHub REST API", Type = "External API", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.GitHub.Pat]) ? "PAT Configured" : "Rate Limited", Purpose = "Repository data access" }
            },
            OpenTelemetry = new[]
            {
                new { Name = "OTLP Exporter", Type = "Telemetry Export", Status = !string.IsNullOrEmpty(configuration[ConfigKeys.Telemetry.OtlpEndpoint]) ? "Configured" : "Not configured", Purpose = "Distributed tracing" }
            }
        };

        return Results.Ok(new
        {
            Environment = env.EnvironmentName,
            Timestamp = DateTime.UtcNow,
            OverallHealth = report.Status.ToString(),
            ExternalConnections = externalConnections,
            Summary = new
            {
                TotalConnections = 6,
                ConfiguredCount = configuredCount,
                ApplicationPurpose = "Repository line tracking, AI code detection, contributor statistics"
            }
        });
    }

    internal static void MapDevOnlyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dev = endpoints.MapGroup("").WithTags("Dev");

        // The former /dev-login/{userId} cookie-minting route is gone: Rule 3.3 makes
        // FakeAuthHandler the single dev/test auth path. Send X-Fake-User (and optionally
        // X-Fake-Roles) on the request instead of signing in first.

        dev.MapPost("/api/log/client", ([FromBody] ClientLogEntry logEntry, ILogger<Program> logger) =>
        {
            var message = $"[CLIENT] {logEntry.Message}";
            switch (logEntry.Level.ToUpperInvariant())
            {
                case "ERROR":
                case "FATAL":
                    logger.LogError(logEntry.Exception, message, logEntry.Properties); break;
                case "WARNING":
                case "WARN":
                    logger.LogWarning(message, logEntry.Properties); break;
                case "INFO":
                case "INFORMATION":
                    logger.LogInformation(message, logEntry.Properties); break;
                case "DEBUG":
                    logger.LogDebug(message, logEntry.Properties); break;
                default:
                    logger.LogInformation(message, logEntry.Properties); break;
            }
            return Results.Ok(new { Status = "Logged" });
        })
        .WithName("LogClientEvent")
        .WithSummary("Accepts client-side log entries (Development only)");
    }
}

// Records for dev/test endpoints — co-located with their mapping
public record ClientLogEntry(
    string Level,
    string Message,
    string? Exception = null,
    Dictionary<string, object>? Properties = null
);


