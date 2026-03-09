using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using PoRepoLineTracker.Api.Telemetry;

namespace PoRepoLineTracker.Api.Extensions;

public static class TelemetryServiceExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddApplicationInsightsTelemetry(options =>
            options.ConnectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: AppTelemetry.SourceName,
                serviceVersion: AppTelemetry.Version))
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
                    string.Equals(configuration["EnableConsoleExporters"], "true", StringComparison.OrdinalIgnoreCase))
                    tracing.AddConsoleExporter();

                var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
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
                    string.Equals(configuration["EnableConsoleExporters"], "true", StringComparison.OrdinalIgnoreCase))
                    metrics.AddConsoleExporter();

                var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
