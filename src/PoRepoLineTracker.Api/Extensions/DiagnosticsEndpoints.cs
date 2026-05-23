using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.ComponentModel.DataAnnotations;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class DiagnosticsEndpoints
{
    internal static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        // /health is served by the registered IHealthCheck pipeline via app.MapHealthChecks("/health")
        // in Program.cs — no custom implementation needed here.

        app.MapGet("/diag", async (IConfiguration configuration, IWebHostEnvironment env, HealthCheckService healthChecks) =>
        {
            static string Mask(string? value)
            {
                if (string.IsNullOrEmpty(value)) return "(not set)";
                if (value.Length <= 6) return new string('*', value.Length);
                var visible = Math.Min(3, value.Length / 4);
                return string.Concat(value.AsSpan(0, visible), "***", value.AsSpan(value.Length - visible));
            }

            // Run all registered health checks to surface live connectivity status
            HealthReport report;
            try
            {
                report = await healthChecks.CheckHealthAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Health check execution failed during /diag request");
                report = new HealthReport(
                    new Dictionary<string, HealthReportEntry>
                    {
                        ["health_check_error"] = new HealthReportEntry(
                            HealthStatus.Unhealthy, ex.Message, TimeSpan.Zero, ex, null)
                    },
                    HealthStatus.Unhealthy,
                    TimeSpan.Zero);
            }

            // Gather all external connections information
            var externalConnections = new
            {
                Azure = new
                {
                    KeyVault = new
                    {
                        Name = "Azure Key Vault",
                        Type = "Secret Storage",
                        Url = configuration["KeyVault:Uri"] ?? "(not configured)",
                        Status = string.IsNullOrEmpty(configuration["KeyVault:Uri"]) ? "Not configured" : "Configured",
                        Purpose = "Securely stores secrets like GitHub PAT, connection strings"
                    },
                    TableStorage = new
                    {
                        Name = "Azure Table Storage",
                        Type = "Data Storage",
                        ServiceUrl = configuration["AzureTableStorage:ServiceUrl"] ?? "(not set)",
                        ConnectionString = Mask(configuration["AzureTableStorage:ConnectionString"]),
                        Status = !string.IsNullOrEmpty(configuration["AzureTableStorage:ServiceUrl"]) ||
                                 !string.IsNullOrEmpty(configuration["AzureTableStorage:ConnectionString"]) ? "Configured" : "Not configured",
                        Purpose = "Stores repository analysis data, commit line counts, user preferences"
                    },
                    AppInsights = new
                    {
                        Name = "Azure Application Insights",
                        Type = "Telemetry & Monitoring",
                        ConnectionString = Mask(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]),
                        Status = string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]) ? "Not configured" : "Configured",
                        Purpose = "Application performance monitoring, crash reporting, usage analytics"
                    }
                },
                GitHub = new
                {
                    OAuth = new
                    {
                        Name = "GitHub OAuth",
                        Type = "Authentication",
                        ClientId = Mask(configuration["GitHub:ClientId"]),
                        Status = string.IsNullOrEmpty(configuration["GitHub:ClientId"]) ? "Not configured" : "Configured",
                        Purpose = "User authentication via GitHub login"
                    },
                    API = new
                    {
                        Name = "GitHub REST API",
                        Type = "External API",
                        BaseUrl = "https://api.github.com/",
                        PAT = Mask(configuration["GitHub:PAT"]),
                        Status = string.IsNullOrEmpty(configuration["GitHub:PAT"]) ? "PAT not configured (rate limited)" : "PAT configured",
                        Purpose = "Repository metadata, commit history, file contents"
                    },
                    Webhooks = new
                    {
                        Name = "GitHub Webhooks",
                        Type = "Event Subscription",
                        Status = "N/A",
                        Purpose = "Real-time repository event notifications (future enhancement)"
                    }
                },
                OpenTelemetry = new
                {
                    OTLP = new
                    {
                        Name = "OpenTelemetry Protocol (OTLP)",
                        Type = "Telemetry Export",
                        Endpoint = Mask(configuration["OpenTelemetry:OtlpEndpoint"]),
                        Status = string.IsNullOrEmpty(configuration["OpenTelemetry:OtlpEndpoint"]) ? "Not configured" : "Configured",
                        Purpose = "Distributed tracing and metrics export to observability platforms"
                    }
                },
                LocalDevelopment = new
                {
                    Azurite = new
                    {
                        Name = "Azurite (Local Emulator)",
                        Type = "Local Emulation",
                        ConnectionString = configuration["AzureTableStorage:ConnectionString"]?.Contains("UseDevelopmentStorage") == true ? "UseDevelopmentStorage=true" : "(not using emulator)",
                        Status = configuration["AzureTableStorage:ConnectionString"]?.Contains("UseDevelopmentStorage") == true ? "Active" : "Not active",
                        Purpose = "Local Azure Table Storage emulation for development"
                    },
                    UserSecrets = new
                    {
                        Name = "ASP.NET Core User Secrets",
                        Type = "Local Configuration",
                        Status = "Active (Development)",
                        Purpose = "Local development secrets override"
                    }
                }
            };

            return Results.Json(new
            {
                Environment = env.EnvironmentName,
                Timestamp = DateTime.UtcNow,
                OverallHealth = report.Status.ToString(),
                // Live connectivity probes — each entry is the result of a real IHealthCheck call
                HealthChecks = report.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        Status = kvp.Value.Status.ToString(),
                        Description = kvp.Value.Description,
                        DurationMs = kvp.Value.Duration.TotalMilliseconds,
                        Error = kvp.Value.Exception?.Message
                    }),
                ExternalConnections = externalConnections,
                Summary = new
                {
                    TotalAzureServices = 3,
                    TotalGitHubIntegrations = 3,
                    TotalLocalServices = 2,
                    Purpose = "Repository analysis, commit tracking, line counting, AI detection, contributor statistics"
                }
            });
        })
        .WithName("Diagnostics")
        .RequireAuthorization();

        // Also expose as /api/diagnostics for consistency
        app.MapGet("/api/diagnostics", async (IConfiguration configuration, IWebHostEnvironment env, HealthCheckService healthChecks) =>
        {
            return await MapDiagnosticsData(configuration, env, healthChecks);
        })
        .WithName("ApiDiagnostics")
        .RequireAuthorization();
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
        if (!string.IsNullOrEmpty(configuration["KeyVault:Uri"])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration["AzureTableStorage:ServiceUrl"]) || !string.IsNullOrEmpty(configuration["AzureTableStorage:ConnectionString"])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration["GitHub:ClientId"])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration["GitHub:PAT"])) configuredCount++;
        if (!string.IsNullOrEmpty(configuration["OpenTelemetry:OtlpEndpoint"])) configuredCount++;

        var externalConnections = new
        {
            Azure = new[]
            {
                new { Name = "Azure Key Vault", Type = "Secret Storage", Status = !string.IsNullOrEmpty(configuration["KeyVault:Uri"]) ? "Configured" : "Not configured", Purpose = "Securely stores secrets" },
                new { Name = "Azure Table Storage", Type = "Data Storage", Status = !string.IsNullOrEmpty(configuration["AzureTableStorage:ServiceUrl"]) || !string.IsNullOrEmpty(configuration["AzureTableStorage:ConnectionString"]) ? "Configured" : "Not configured", Purpose = "Stores repository analysis data" },
                new { Name = "Azure Application Insights", Type = "Telemetry", Status = !string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]) ? "Configured" : "Not configured", Purpose = "Performance monitoring" }
            },
            GitHub = new[]
            {
                new { Name = "GitHub OAuth", Type = "Authentication", Status = !string.IsNullOrEmpty(configuration["GitHub:ClientId"]) ? "Configured" : "Not configured", Purpose = "User authentication" },
                new { Name = "GitHub REST API", Type = "External API", Status = !string.IsNullOrEmpty(configuration["GitHub:PAT"]) ? "PAT Configured" : "Rate Limited", Purpose = "Repository data access" }
            },
            OpenTelemetry = new[]
            {
                new { Name = "OTLP Exporter", Type = "Telemetry Export", Status = !string.IsNullOrEmpty(configuration["OpenTelemetry:OtlpEndpoint"]) ? "Configured" : "Not configured", Purpose = "Distributed tracing" }
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

    internal static void MapDevOnlyEndpoints(this WebApplication app)
    {
        app.MapGet("/dev-login/{userId}", async (Guid userId, HttpContext context, IUserService userService) =>
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

        app.MapPost("/api/log/client", ([FromBody] ClientLogEntry logEntry, ILogger<Program> logger) =>
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


