using Polly;
using Polly.CircuitBreaker;
using System.Net;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Services.LineCounters;
using Microsoft.AspNetCore.HttpOverrides;
using Azure.Identity;

namespace PoRepoLineTracker.Api.Extensions;

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
            var connStr = config["AzureTableStorage:ConnectionString"] ?? config["ConnectionStrings:tables"];

            if (!string.IsNullOrEmpty(connStr) && connStr.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
                return new Azure.Data.Tables.TableServiceClient(connStr);

            var storageEndpoint = config["AzureTableStorage:ServiceUrl"];
            if (!string.IsNullOrEmpty(storageEndpoint))
                return new Azure.Data.Tables.TableServiceClient(new Uri(storageEndpoint), new DefaultAzureCredential());

            if (!string.IsNullOrEmpty(connStr))
                return new Azure.Data.Tables.TableServiceClient(connStr);

            return new Azure.Data.Tables.TableServiceClient("UseDevelopmentStorage=true");
        });

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

        // JSON case-insensitive deserialization
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNameCaseInsensitive = true);

        // GitHub API HttpClient with Polly circuit breaker
        var circuitBreakerOptions = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(10),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = args => new ValueTask<bool>(
                args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable ||
                args.Outcome.Result?.StatusCode == HttpStatusCode.RequestTimeout)
        };

        services.AddHttpClient("GitHubClient", (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.DefaultRequestHeaders.Add("User-Agent", "PoRepoLineTracker");
            var pat = config["GitHub:PAT"];
            if (!string.IsNullOrEmpty(pat))
                client.DefaultRequestHeaders.Add("Authorization", $"token {pat}");
        })
        .AddResilienceHandler("CircuitBreaker", b => b.AddCircuitBreaker(circuitBreakerOptions));

        // Domain services
        services.AddScoped<PoRepoLineTracker.Infrastructure.Interfaces.IGitClient, PoRepoLineTracker.Infrastructure.Services.GitClient>();
        services.AddScoped<ILineCounter, DefaultLineCounter>();
        services.AddScoped<ILineCounter, CSharpLineCounter>();
        services.AddScoped<PoRepoLineTracker.Infrastructure.FileFilters.IFileIgnoreFilter, PoRepoLineTracker.Infrastructure.FileFilters.FileIgnoreFilter>();
        services.AddScoped<IFailedOperationService, PoRepoLineTracker.Infrastructure.Services.FailedOperationService>();
        services.AddHostedService<PoRepoLineTracker.Infrastructure.Services.FailedOperationBackgroundService>();

        services.AddScoped<IGitHubService>(sp => new PoRepoLineTracker.Infrastructure.Services.GitHubService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("GitHubClient"),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<PoRepoLineTracker.Infrastructure.Services.GitHubService>>(),
            sp.GetServices<ILineCounter>(),
            sp.GetRequiredService<PoRepoLineTracker.Infrastructure.Interfaces.IGitClient>(),
            sp.GetRequiredService<PoRepoLineTracker.Infrastructure.FileFilters.IFileIgnoreFilter>()));

        services.AddScoped<IRepositoryDataService, PoRepoLineTracker.Infrastructure.Services.RepositoryDataService>();
        services.AddScoped<IUserService, PoRepoLineTracker.Infrastructure.Services.UserService>();
        services.AddScoped<IUserPreferencesService, PoRepoLineTracker.Infrastructure.Services.UserPreferencesService>();

        // MediatR — register all handlers from the Application assembly
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand).Assembly));

        // Health checks
        services.AddHealthChecks()
            .AddCheck<PoRepoLineTracker.Api.HealthChecks.AzureTableStorageHealthCheck>("azure_table_storage");

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
