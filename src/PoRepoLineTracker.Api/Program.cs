using Serilog;
using PoRepoLineTracker.Api.Extensions;
using PoRepoLineTracker.Api.Middleware;
using PoRepoLineTracker.Application.Telemetry;
using Scalar.AspNetCore;
using Azure.Identity;

namespace PoRepoLineTracker.Api
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                var app = CreateWebApplication(args);
                app.Run();
            }
            catch (Exception ex) when (ex is not HostAbortedException)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static WebApplication CreateWebApplication(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Azure Key Vault — managed identity in production; DefaultAzureCredential locally
            var keyVaultUrl = builder.Configuration["KeyVault:Uri"];
            if (!string.IsNullOrEmpty(keyVaultUrl))
            {
                builder.Configuration.AddAzureKeyVault(
                    new Uri(keyVaultUrl),
                    new DefaultAzureCredential(),
                    new PrefixKeyVaultSecretManager());
                Log.Information("Azure Key Vault configuration loaded from {KeyVaultUrl}", keyVaultUrl);
            }
            else
            {
                Log.Warning("KeyVault:Url not configured — secrets must come from user-secrets or environment variables");
            }

            // Local developer override (not committed).
            // Loaded unconditionally (optional: true) so that secrets are available
            // regardless of ASPNETCORE_ENVIRONMENT — prevents the "GitHub:ClientId is
            // not configured" 500 when the env var is accidentally omitted at startup.
            builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

            // Structured logging via Serilog
            builder.Host.UseSerilog((context, services, cfg) =>
            {
                cfg
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "PoRepoLineTracker")
                    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                    .Filter.ByExcluding(e => e.MessageTemplate.Text.Contains("license key") || e.MessageTemplate.Text.Contains("Lucky Penny"))
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                    .MinimumLevel.Information();

                if (context.HostingEnvironment.IsDevelopment())
                {
                    cfg.WriteTo.File("log.txt",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        shared: true,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
                }

                var appInsightsConn = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
                // AppInsights telemetry handled by AddApplicationInsightsTelemetry() in AddTelemetry().
            });

            // Raise Kestrel body-size limit to 600 MB to allow large ZIP uploads
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 600 * 1024 * 1024; // 600 MB
            });

            // Service registrations via extension methods
            builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
            builder.Services.AddAuth(builder.Configuration, builder.Environment);
            builder.Services.AddTelemetry(builder.Configuration, builder.Environment);

            var app = builder.Build();

            // #4 fix: wire ObservableGauge callbacks so telemetry meters are actually observed
            AppTelemetry.InitializeGauges(
                getTotalRepositories: () => 0,
                getPendingAnalysis: () => 0);

            // Middleware pipeline
            app.UseForwardedHeaders();
            app.UseMiddleware<LogEnrichmentMiddleware>();
            app.UseMiddleware<SecurityHeadersMiddleware>();
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference(options =>
                {
                    options.WithTitle("PoRepoLineTracker API");
                    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
                });
            }

            // Only redirect to HTTPS in non-Development environments.
            // In Development the http profile runs without an HTTPS URL, so the
            // middleware can't determine the redirect port and logs a WRN on every request.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            // All API route mappings - MUST come BEFORE fallback file
            app.MapApiEndpoints();

            app.MapHealthChecks("/health");
            app.MapFallbackToFile("index.html");

            return app;
        }
    }
}