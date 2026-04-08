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
                Configuration = new
                {
                    KeyVault = new { Url = configuration["KeyVault:Url"] ?? "(not set)" },
                    AzureTableStorage = new
                    {
                        ServiceUrl = Mask(configuration["AzureTableStorage:ServiceUrl"]),
                        ConnectionString = Mask(configuration["AzureTableStorage:ConnectionString"])
                    },
                    GitHub = new
                    {
                        ClientId = Mask(configuration["GitHub:ClientId"]),
                        ClientSecret = Mask(configuration["GitHub:ClientSecret"]),
                        PAT = Mask(configuration["GitHub:PAT"]),
                        CallbackPath = configuration["GitHub:CallbackPath"]
                    },
                    ApplicationInsights = new { ConnectionString = Mask(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]) },
                    OpenTelemetry = new { OtlpEndpoint = Mask(configuration["OpenTelemetry:OtlpEndpoint"]) }
                }
            });
        })
        .WithName("Diagnostics")
        .RequireAuthorization();
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

        app.MapPost("/test-login", async (TestLoginRequest request, HttpContext context) =>
        {
            try
            {
                var userId = Guid.NewGuid();
                // #9 fix: guard against missing '@' in email rather than IndexOutOfRange
                var atIndex = request.Email.IndexOf('@');
                var username = atIndex > 0 ? request.Email[..atIndex] : request.Email;

                var identity = new System.Security.Claims.ClaimsIdentity(
                [
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                    new("UserId", userId.ToString()),
                    new(System.Security.Claims.ClaimTypes.Name, username),
                    new(System.Security.Claims.ClaimTypes.Email, request.Email),
                    new("DisplayName", username),
                    new("AvatarUrl", ""),
                    new("UserAgent", request.UserAgent ?? "TestClient")
                ], CookieAuthenticationDefaults.AuthenticationScheme);

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new System.Security.Claims.ClaimsPrincipal(identity));

                Log.Information("Test login successful for email {Email}", request.Email);
                return Results.Ok(new { success = true, message = "Login successful", userId });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Test login failed for email {Email}", request.Email);
                return Results.Problem($"Test login failed: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("TestLogin")
        .AllowAnonymous()
        .WithSummary("Quick test login endpoint (Development only)");

        app.MapGet("/test-login-redirect", async (string email, string? password, HttpContext context) =>
        {
            try
            {
                var userId = Guid.NewGuid();
                // #9 fix: guard against missing '@' in email query parameter
                var atIndex = email.IndexOf('@');
                var username = atIndex > 0 ? email[..atIndex] : email;

                var identity = new System.Security.Claims.ClaimsIdentity(
                [
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                    new("UserId", userId.ToString()),
                    new(System.Security.Claims.ClaimTypes.Name, username),
                    new(System.Security.Claims.ClaimTypes.Email, email),
                    new("DisplayName", username),
                    new("AvatarUrl", ""),
                    new("UserAgent", "BrowserTest")
                ], CookieAuthenticationDefaults.AuthenticationScheme);

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new System.Security.Claims.ClaimsPrincipal(identity));

                Log.Information("Test login redirect successful for email {Email}", email);
                return Results.Redirect("/");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Test login redirect failed for email {Email}", email);
                return Results.Problem($"Test login redirect failed: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("TestLoginRedirect")
        .AllowAnonymous()
        .WithSummary("Browser-based test login (Development only)");

        app.MapPost("/api/log/client", ([FromBody] ClientLogEntry logEntry, ILogger<Program> logger) =>
        {
            var message = $"[CLIENT] {logEntry.Message}";
            switch (logEntry.Level.ToUpperInvariant())
            {
                case "ERROR": case "FATAL":
                    logger.LogError(logEntry.Exception, message, logEntry.Properties); break;
                case "WARNING": case "WARN":
                    logger.LogWarning(message, logEntry.Properties); break;
                case "INFO": case "INFORMATION":
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

// #9 fix: [Required][EmailAddress] prevents null/non-email crash in username derivation
public record TestLoginRequest(
    [Required][EmailAddress] string Email,
    string? Password = null,
    string? UserAgent = null
);
