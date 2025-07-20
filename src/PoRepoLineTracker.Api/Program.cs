using Serilog;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Polly.CircuitBreaker; // Explicitly add this using directive
using PoRepoLineTracker.Domain.Models; // Add this line

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

    // Register application services
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IGitHubService, PoRepoLineTracker.Infrastructure.Services.GitHubService>();
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryDataService, PoRepoLineTracker.Infrastructure.Services.RepositoryDataService>();
    builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryService, PoRepoLineTracker.Application.Services.RepositoryService>();

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

    builder.Services.AddHttpClient("GitHubClient", client =>
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        client.DefaultRequestHeaders.Add("User-Agent", "PoRepoLineTracker");
        // GitHub PAT will be added here via configuration later
    })
    .AddResilienceHandler("CircuitBreaker", builder =>
    {
        builder.AddCircuitBreaker(circuitBreakerOptions);
    });

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

    app.MapPost("/api/repositories/{repositoryId}/analyze", async (Guid repositoryId, IRepositoryService repoService) =>
    {
        await repoService.AnalyzeRepositoryCommitsAsync(repositoryId);
        return Results.Accepted();
    })
    .WithName("AnalyzeRepository");

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
