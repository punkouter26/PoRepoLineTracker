using PoRepoLineTracker.API.Features.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Azure.Identity;
using FluentValidation;

namespace PoRepoLineTracker.API.Extensions;

/// <summary>
/// SOLID — Composition Root: Centralizes all DI registrations as required by the
/// Composition Root pattern (Mark Seemann). All service lifetimes, decorators,
/// and interceptor configurations are defined in one place.
/// 
/// SOLID — Dependency Inversion Principle: Services are registered by their
/// application-layer interfaces (I*) and resolved to infrastructure implementations.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Azure Table Storage — Azurite, cloud endpoint, or connection string fallback
        services.AddSingleton<Azure.Data.Tables.TableServiceClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connStr = config[ConfigKeys.AzureTableStorage.ConnectionString] ?? config[ConfigKeys.AzureTableStorage.TablesConnectionString];

            if (!string.IsNullOrEmpty(connStr) && connStr.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
                return new Azure.Data.Tables.TableServiceClient(connStr);

            var storageEndpoint = config[ConfigKeys.AzureTableStorage.ServiceUrl];
            if (!string.IsNullOrEmpty(storageEndpoint))
                return new Azure.Data.Tables.TableServiceClient(new Uri(storageEndpoint), new DefaultAzureCredential());

            if (!string.IsNullOrEmpty(connStr))
                return new Azure.Data.Tables.TableServiceClient(connStr);

            return new Azure.Data.Tables.TableServiceClient("UseDevelopmentStorage=true");
        });

        // Rule 2.2 — FluentValidation rules (defined in .Shared) for request DTOs.
        services.AddValidatorsFromAssemblyContaining<BulkRepositoryDtoValidator>();

        // OpenAPI
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "PoRepoLineTracker API";
                document.Info.Version = "v1";
                document.Info.Description = "API for tracking repository line counts and code statistics";
                return Task.CompletedTask;
            });
        });

        // Rule 1.2 — source-generated JSON metadata ahead of the reflection resolver.
        //
        // Inserting at index 0 means every type declared in AppJsonSerializerContext (i.e. every
        // wire contract) is resolved from generated metadata; the default reflection resolver
        // stays behind it as the tail so the dev-only client-log endpoint, whose payload carries
        // a Dictionary<string, object>, still binds. Server-side there is no trimming benefit to
        // removing that tail — the client is where the reflection-free path matters, and it uses
        // AppJsonOptions.Default, which has no fallback at all.
        //
        // PropertyNameCaseInsensitive is no longer set here: JsonSerializerDefaults.Web (which
        // minimal APIs already apply, and which the generated context declares) implies it.
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

        // Rule 4.2 — antiforgery for every state-changing endpoint (see AntiforgeryMiddleware).
        //
        // HeaderName is what makes this work for a JSON SPA at all: the default validator reads
        // the request token from a form field, and none of these endpoints post a form. With a
        // header configured the pair becomes a standard double-submit — the cookie half stays
        // HttpOnly, and the client echoes the other half from /api/antiforgery/token.
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-PoRepoLineTracker.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // Rule 5.4 — HybridCache is the single caching abstraction. It is in-memory only here
        // (no IDistributedCache registered), which is correct for a single App Service instance
        // and gains an L2 for free if Redis is ever added. Its stampede protection matters more
        // than raw hit rate: GitHub's REST API is rate-limited per token, so N concurrent page
        // loads must produce one upstream call, not N.
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });

        services.AddHttpClient("GitHubClient", (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.DefaultRequestHeaders.Add("User-Agent", "PoRepoLineTracker");
            var pat = config[ConfigKeys.GitHub.Pat];
            if (!string.IsNullOrEmpty(pat))
                client.DefaultRequestHeaders.Add("Authorization", $"token {pat}");
        })
        // Rule 5.4 — the standard pipeline replaces the hand-rolled circuit-breaker-only handler:
        // rate limiter → total timeout → retry → circuit breaker → attempt timeout. The retry is
        // what the old configuration was missing; a single 503 from GitHub used to surface to the
        // user instead of being retried with backoff.
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.UseJitter = true;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });

        // Domain services
        services.AddScoped<IGitClient, GitClient>();
        services.AddScoped<ILineCounter, DefaultLineCounter>();
        services.AddScoped<IFileIgnoreFilter, FileIgnoreFilter>();

        // Singleton: tracks live analysis progress across background Task.Run jobs
        services.AddSingleton<IAnalysisProgressService, AnalysisProgressService>();

        services.AddScoped<IGitHubService>(sp => new GitHubService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("GitHubClient"),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<GitHubService>>(),
            sp.GetServices<ILineCounter>(),
            sp.GetRequiredService<IGitClient>(),
            sp.GetRequiredService<IFileIgnoreFilter>()));

        services.AddScoped<IRepositoryDataService, RepositoryDataService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();
        services.AddScoped<IAiDetectionService, AiDetectionService>();

        // MediatR — register every handler in this assembly (all slices live here now)
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(Program).Assembly));

        // Health checks
        services.AddHealthChecks()
            .AddCheck<AzureTableStorageHealthCheck>("azure_table_storage");

        // Forwarded headers for Azure Container Apps reverse proxy
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
