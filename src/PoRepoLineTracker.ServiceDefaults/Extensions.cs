using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Aspire ServiceDefaults extensions for PoRepoLineTracker.
/// Provides standardized observability, resilience, and health checks.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds Aspire service defaults including OpenTelemetry, health checks, and resilience.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Enable resilience by default
            http.AddStandardResilienceHandler();
            // Enable service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment for HTTP client logging during development
        // builder.Services.AddHttpClientLogger();

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry with tracing and metrics.
    /// </summary>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                // Only add essential metrics to reduce noise
                metrics.AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation();
                // Removed: .AddHttpClientInstrumentation() - reduces verbose HTTP client metrics
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                       .AddAspNetCoreInstrumentation()
                       // Filter out noisy health check traces
                       .AddHttpClientInstrumentation(options =>
                       {
                           options.FilterHttpRequestMessage = (httpRequestMessage) =>
                           {
                               // Filter out health check and telemetry requests
                               var path = httpRequestMessage.RequestUri?.AbsolutePath ?? "";
                               return !path.Contains("health") && !path.Contains("otlp");
                           };
                       });
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds default health check endpoints for liveness and readiness.
    /// </summary>
    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps Aspire default endpoints including health checks.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints are centralized to the `/health` aggregation endpoint in the API.
        // Mapping of separate `/health/live` or `/health/ready` endpoints has been removed to enforce a single health contract.

        return app;
    }
}
