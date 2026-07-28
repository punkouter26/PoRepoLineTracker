using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.ComponentModel.DataAnnotations;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Serilog;
using PoRepoLineTracker.Shared.Models;

namespace PoRepoLineTracker.Api.Extensions;

internal static class DiagnosticsEndpoints
{
    internal static void MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // /health is served by the registered IHealthCheck pipeline via app.MapHealthChecks("/health")
        // in Program.cs — no custom implementation needed here.

        // /diag is served by the Blazor WASM client (a hidden, authenticated diagnostics page
        // that renders the masked connection statuses). The raw JSON it consumes lives at
        // /api/diagnostics below — keeping a single source of diagnostics data.
        var diagnostics = endpoints.MapGroup("/api/diagnostics")
            .WithTags("Diagnostics")
            .RequireAuthorization();

        diagnostics.MapGet("/", async (IConfiguration configuration, IWebHostEnvironment env, HealthCheckService healthChecks) =>
        {
            return await MapDiagnosticsData(configuration, env, healthChecks);
        })
        .WithName("ApiDiagnostics");
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

        dev.MapGet("/dev-login/{userId}", async (UserId userId, HttpContext context, IUserService userService) =>
        {
            try
            {
                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    user = new User
                    {
                        Id = userId,
                        Username = $"TestUser-{userId:N}",
                        DisplayName = $"Test User {userId:N}",
                        AvatarUrl = "",
                        Email = $"testuser{userId:N}@example.com",
                        AccessToken = "test-token"
                    };
                    user = await userService.UpsertUserAsync(user);
                }

                var identity = new System.Security.Claims.ClaimsIdentity(
                [
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                    new("UserId", userId.ToString()),
                    new(System.Security.Claims.ClaimTypes.Name, user.Username)
                ], CookieAuthenticationDefaults.AuthenticationScheme);

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new System.Security.Claims.ClaimsPrincipal(identity));

                Log.Information("Dev login successful for test user {UserId}", userId);
                return Results.Redirect("/");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Dev login failed for user {UserId}", userId);
                return Results.Problem($"Dev login failed: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("DevLogin")
        .AllowAnonymous()
        .WithSummary("Development-only endpoint to bypass GitHub OAuth");

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


