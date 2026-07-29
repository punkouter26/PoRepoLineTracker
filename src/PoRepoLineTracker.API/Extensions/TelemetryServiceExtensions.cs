using System.Reflection;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace PoRepoLineTracker.API.Extensions;

public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Hardcoded staging Application Insights connection string used as the final fallback
    /// when neither APPLICATIONINSIGHTS_CONNECTION_STRING nor APPINSIGHTS_INSTRUMENTATIONKEY
    /// is configured (Rule 8). Left empty by default so local/dev runs do not emit to a shared
    /// staging resource; set it to a real staging connection string to enable the fallback.
    /// </summary>
    private const string StagingFallbackConnectionString = "";

    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Connection-string resolution order (Rule 8):
        //   1. APPLICATIONINSIGHTS_CONNECTION_STRING
        //   2. APPINSIGHTS_INSTRUMENTATIONKEY  (promoted to a connection string)
        //   3. Hardcoded staging connection string fallback
        var aiCs = configuration[ConfigKeys.Telemetry.AppInsightsConnectionString]
                   ?? configuration[ConfigKeys.Telemetry.AppInsightsConnectionStringSection];

        if (string.IsNullOrWhiteSpace(aiCs))
        {
            var iKey = configuration[ConfigKeys.Telemetry.AppInsightsInstrumentationKey]
                       ?? configuration[ConfigKeys.Telemetry.AppInsightsInstrumentationKeySection];
            if (!string.IsNullOrWhiteSpace(iKey))
                aiCs = $"InstrumentationKey={iKey}";
        }

        if (string.IsNullOrWhiteSpace(aiCs) && !string.IsNullOrWhiteSpace(StagingFallbackConnectionString))
            aiCs = StagingFallbackConnectionString;

        // cloud_RoleName mapping (Rule 8): resolve the real assembly name via reflection so the
        // App Insights "cloud_RoleName" never falls back to "unknown_service:dotnet".
        var roleName = Assembly.GetEntryAssembly()?.GetName().Name
                       ?? AppTelemetry.SourceName;
        var roleVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                          ?? AppTelemetry.Version;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: roleName,
                serviceVersion: roleVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(AppTelemetry.SourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (environment.IsDevelopment() &&
                    string.Equals(configuration[ConfigKeys.Telemetry.EnableConsoleExporters], "true", StringComparison.OrdinalIgnoreCase))
                    tracing.AddConsoleExporter();

                var otlpEndpoint = configuration[ConfigKeys.Telemetry.OtlpEndpoint];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(AppTelemetry.SourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (environment.IsDevelopment() &&
                    string.Equals(configuration[ConfigKeys.Telemetry.EnableConsoleExporters], "true", StringComparison.OrdinalIgnoreCase))
                    metrics.AddConsoleExporter();

                var otlpEndpoint = configuration[ConfigKeys.Telemetry.OtlpEndpoint];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        // Only register Azure Monitor when a connection string is configured.
        // Calling UseAzureMonitor() without a connection string throws at host startup.
        if (!string.IsNullOrWhiteSpace(aiCs))
        {
            services.AddOpenTelemetry()
                .UseAzureMonitor(o =>
                {
                    o.ConnectionString = aiCs;

                    // Live Metrics / QuickPulse stays active globally for real-time CPU,
                    // memory and traffic-spike visibility (Rule 8).
                    o.EnableLiveMetrics = true;

                    // Sampling profile (Rule 8): full fidelity (100%) in Dev/Test so nothing
                    // is dropped during debugging and E2E runs; capped to ~10% in Production.
                    // Note: the Azure Monitor OTel distro applies a single trace-level rate;
                    // RecordException above ensures exception detail is preserved on sampled-in
                    // traces. (Per-signal "exceptions always 100%" is not separately tunable in
                    // the OTel distro, unlike the legacy adaptive sampler.)
                    o.SamplingRatio = environment.IsProduction() ? 0.1f : 1.0f;
                });
        }

        return services;
    }
}
