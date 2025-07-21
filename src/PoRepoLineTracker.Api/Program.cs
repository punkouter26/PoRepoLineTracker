using Serilog;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Polly.CircuitBreaker; // Explicitly add this using directive

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
    .MinimumLevel.Debug()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog(); // Use Serilog for logging

    // Add services to the container.
    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    // Configure HttpClient with Polly Circuit Breaker
    var circuitBreakerOptions = new CircuitBreakerStrategyOptions<HttpResponseMessage> // Specify TResult
    {
        FailureRatio = 0.5, // Break if 50% of requests fail
        SamplingDuration = TimeSpan.FromSeconds(10), // Sample failures over 10 seconds
        MinimumThroughput = 5, // Need at least 5 requests in sampling duration to break
        BreakDuration = TimeSpan.FromSeconds(30), // Break for 30 seconds
        ShouldHandle = args =>
        {
            return new ValueTask<bool>(args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable ||
                                       args.Outcome.Result?.StatusCode == HttpStatusCode.RequestTimeout);
        }
    };

    builder.Services.AddHttpClient("GitHubClient", (serviceProvider, client) =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var gitHubPat = configuration["GitHub:PAT"];
        
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        client.DefaultRequestHeaders.Add("User-Agent", "PoRepoLineTracker");
        
        if (!string.IsNullOrEmpty(gitHubPat))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"token {gitHubPat}");
        }
    })
    .AddResilienceHandler("CircuitBreaker", builder =>
    {
        builder.AddCircuitBreaker(circuitBreakerOptions);
    });

    // Register application services with proper HttpClient injection
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IGitHubService>(serviceProvider =>
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("GitHubClient");
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var logger = serviceProvider.GetRequiredService<ILogger<PoRepoLineTracker.Infrastructure.Services.GitHubService>>();
        return new PoRepoLineTracker.Infrastructure.Services.GitHubService(httpClient, configuration, logger);
    });
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryDataService, PoRepoLineTracker.Infrastructure.Services.RepositoryDataService>();
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryService, PoRepoLineTracker.Application.Services.RepositoryService>();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");

    // API Endpoints
    app.MapPost("/api/repositories", async (GitHubRepository newRepo, IRepositoryService repoService) =>
    {
        var repo = await repoService.AddRepositoryAsync(newRepo.Owner, newRepo.Name, newRepo.CloneUrl);
        return Results.Created($"/api/repositories/{repo.Id}", repo);
    })
    .WithName("AddRepository");

    app.MapGet("/api/repositories", async (IRepositoryService repoService) =>
    {
        var repositories = await repoService.GetAllRepositoriesAsync();
        return Results.Ok(repositories);
    })
    .WithName("GetAllRepositories");

    app.MapGet("/api/repositories/{repositoryId}/linecounts", async (Guid repositoryId, IRepositoryService repoService) =>
    {
        var lineCounts = await repoService.GetLineCountsForRepositoryAsync(repositoryId);
        return Results.Ok(lineCounts);
    })
    .WithName("GetRepositoryLineCounts");

    app.MapGet("/api/repositories/{repositoryId}/linehistory/{days}", async (Guid repositoryId, int days, IRepositoryService repoService) =>
    {
        try
        {
            var lineHistory = await repoService.GetLineCountHistoryAsync(repositoryId, days);
            return Results.Ok(lineHistory);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving line count history for repository {RepositoryId}", repositoryId);
            return Results.Problem($"Error retrieving line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("GetRepositoryLineHistory");

    app.MapGet("/api/repositories/allcharts/{days}", async (int days, IRepositoryService repoService) =>
    {
        try
        {
            var allChartsData = await repoService.GetAllRepositoriesLineCountHistoryAsync(days);
            return Results.Ok(allChartsData);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving line count history for all repositories");
            return Results.Problem($"Error retrieving all repositories line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("GetAllRepositoriesLineHistory");

    app.MapGet("/api/settings/file-extensions", async (IRepositoryService repoService) =>
    {
        try
        {
            var extensions = await repoService.GetConfiguredFileExtensionsAsync();
            return Results.Ok(extensions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving configured file extensions.");
            return Results.Problem($"Error retrieving configured file extensions: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("GetConfiguredFileExtensions");

    app.MapGet("/api/repositories/{repositoryId}/file-extension-percentages", async (Guid repositoryId, IRepositoryService repoService) =>
    {
        try
        {
            var percentages = await repoService.GetFileExtensionPercentagesAsync(repositoryId);
            return Results.Ok(percentages);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving file extension percentages for repository {RepositoryId}", repositoryId);
            return Results.Problem($"Error retrieving file extension percentages: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("GetFileExtensionPercentages");

    app.MapDelete("/api/repositories/{repositoryId}", async (Guid repositoryId, IRepositoryService repoService) =>
    {
        try
        {
            await repoService.DeleteRepositoryAsync(repositoryId);
            Log.Information("Repository {RepositoryId} deleted successfully via API.", repositoryId);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("Repository {RepositoryId} not found for deletion.", repositoryId);
            return Results.NotFound($"Repository with ID {repositoryId} not found.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting repository {RepositoryId}", repositoryId);
            return Results.Problem($"Error deleting repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("DeleteRepository");

    app.MapGet("/api/github/user-repositories", async (IGitHubService githubService) =>
    {
        try
        {
            var userRepositories = await githubService.GetUserRepositoriesAsync();
            return Results.Ok(userRepositories);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("GitHub PAT not configured: {ErrorMessage}", ex.Message);
            return Results.BadRequest($"GitHub configuration error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching user repositories from GitHub API");
            return Results.Problem($"Error fetching user repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("GetUserRepositories");

    // Health Check Endpoints
    app.MapGet("/api/health/azure-table-storage", async (IRepositoryDataService repoDataService) =>
    {
        try
        {
            // Attempt a simple operation to check connectivity, e.g., try to list a non-existent table
            // This will throw an exception if connectivity fails
            await repoDataService.CheckConnectionAsync();
            return Results.Ok("Azure Table Storage: Connected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Azure Table Storage health check failed.");
            return Results.Problem($"Azure Table Storage: Disconnected - {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("CheckAzureTableStorageHealth");

    app.MapGet("/api/health/github-api", async (IGitHubService githubService) =>
    {
        try
        {
            // Attempt a simple operation to check connectivity, e.g., get a public repository
            await githubService.CheckConnectionAsync();
            return Results.Ok("GitHub API: Connected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GitHub API health check failed.");
            return Results.Problem($"GitHub API: Disconnected - {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
        }
    })
    .WithName("CheckGitHubApiHealth");

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
