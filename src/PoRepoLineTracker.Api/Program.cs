using Serilog;
using PoRepoLineTracker.Api.Extensions;
using PoRepoLineTracker.Api.Middleware;
using PoRepoLineTracker.Api.Telemetry;
using Scalar.AspNetCore;
using Azure.Identity;

namespace PoRepoLineTracker.Api
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                var app = CreateWebApplication(args);
                app.Run();
            }
            catch (Exception ex)
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
            var keyVaultUrl = builder.Configuration["KeyVault:Url"];
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

            // Local developer override (not committed)
            if (builder.Environment.IsDevelopment())
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
                if (!string.IsNullOrWhiteSpace(appInsightsConn))
                    cfg.WriteTo.ApplicationInsights(appInsightsConn, TelemetryConverter.Traces);
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

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");

            // All API route mappings
            app.MapApiEndpoints();

            return app;
        }
    }
}