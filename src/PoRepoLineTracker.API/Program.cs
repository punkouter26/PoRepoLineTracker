using Serilog;
using Scalar.AspNetCore;
using Azure.Identity;
using PoRepoLineTracker.API.Hubs;

namespace PoRepoLineTracker.API
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
            var keyVaultUrl = builder.Configuration[ConfigKeys.KeyVault.Uri];
            if (!string.IsNullOrEmpty(keyVaultUrl))
            {
                try
                {
                    builder.Configuration.AddAzureKeyVault(
                        new Uri(keyVaultUrl),
                        new DefaultAzureCredential(),
                        new PrefixKeyVaultSecretManager());
                    Log.Information("Azure Key Vault configuration loaded from {KeyVaultUrl}", keyVaultUrl);
                }
                catch (Exception ex) when (ex is Azure.Identity.CredentialUnavailableException or System.AggregateException)
                {
                    Log.Warning(ex, "Azure Key Vault unavailable — secrets must come from user-secrets, environment variables, or appsettings.Development.local.json. KeyVault:Uri={KeyVaultUrl}", keyVaultUrl);
                }
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

                var appInsightsConn = context.Configuration[ConfigKeys.Telemetry.AppInsightsConnectionString];
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
                // AllowAnonymous: the FallbackPolicy (Rule 3.3) would otherwise put the local
                // API reference behind a login, which defeats its purpose during development.
                app.MapOpenApi().AllowAnonymous();
                app.MapScalarApiReference(options =>
                {
                    options.WithTitle("PoRepoLineTracker API");
                    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
                }).AllowAnonymous();
            }

            // Only redirect to HTTPS in non-Development environments.
            // In Development the http profile runs without an HTTPS URL, so the
            // middleware can't determine the redirect port and logs a WRN on every request.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            // Static assets MUST be served before UseAuthorization. The FallbackPolicy (Rule 3.3)
            // is applied by the authorization middleware to requests that matched no endpoint —
            // and static files are served by middleware, not endpoints. With these two calls after
            // UseAuthorization every asset (css/app.css, _framework/*) answered 302-to-login, so the
            // browser got a login page instead of the Blazor runtime and rendered the "unhandled
            // error" shell. Serving them first also matches the documented middleware order.
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseAuthentication();
            app.UseAuthorization();

            // Rule 13 — Production Auth Enforcement: require Microsoft/GitHub OAuth in Production.
            // In Development this is a no-op (GUEST mode and local testing still work).
            app.UseMiddleware<ProductionAuthEnforcementMiddleware>();

            // Rule 4.2 — antiforgery on every state-changing /api endpoint.
            //
            // Ordered AFTER UseAuthorization deliberately: an unauthenticated POST then still
            // answers 401 rather than 400, which is both the more useful diagnosis and what the
            // API tier asserts. It also has to sit after UseRouting (implicit here) so that
            // GetEndpoint() can see the SkipAntiforgery metadata for the matched endpoint.
            app.UseMiddleware<AntiforgeryMiddleware>();

            // All API route mappings - MUST come BEFORE fallback file
            app.MapApiEndpoints();

            // Live analysis progress. Deliberately outside /api: AntiforgeryMiddleware validates
            // every unsafe method under that prefix, and the SignalR negotiate handshake is a POST
            // that carries no CSRF token. The hub is [Authorize] and scopes every connection by the
            // caller's own claim, so it is not relying on the prefix for protection.
            app.MapHub<AnalysisHub>("/hubs/analysis");

            // Both must opt out of the FallbackPolicy (Rule 3.3): /health is polled by the
            // deploy smoke test and Azure's probe with no credential, and the fallback file is
            // the Blazor shell itself — gating it would make the login page unreachable.
            app.MapHealthChecks("/health").AllowAnonymous();
            app.MapFallbackToFile("index.html").AllowAnonymous();

            return app;
        }
    }
}