using Serilog;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Polly.CircuitBreaker; // Explicitly add this using directive
using PoRepoLineTracker.Api.Middleware;
using PoRepoLineTracker.Application.Services.LineCounters; // Add this using statement
using MediatR; // Add this using statement
using PoRepoLineTracker.Client.Models; // Add this using statement
using System.Text.Json; // Add this for JSON serialization
using Microsoft.AspNetCore.Mvc; // Add this for FromBody attribute
using Microsoft.AspNetCore.OpenApi; // Add this for WithOpenApi
using Microsoft.OpenApi.Models; // Add this for Swagger
using Swashbuckle.AspNetCore.Annotations; // Add this for EnableAnnotations
using System.Collections.Generic; // Add this for List<object>

namespace PoRepoLineTracker.Api
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
                .MinimumLevel.Debug()
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

            // Add local development configuration
            if (builder.Environment.IsDevelopment())
            {
                builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
            }

            builder.Host.UseSerilog(); // Use Serilog for logging

            // Add services to the container.
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new()
                {
                    Title = "PoRepoLineTracker API",
                    Version = "v1",
                    Description = "API for tracking repository line counts and code statistics"
                });
                c.EnableAnnotations();
            });

            // Configure JSON options for case-insensitive property matching
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

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
            builder.Services.AddScoped<PoRepoLineTracker.Infrastructure.Interfaces.IGitClient, PoRepoLineTracker.Infrastructure.Services.GitClient>(); // Register IGitClient

            // Register ILineCounter implementations
            builder.Services.AddScoped<ILineCounter, DefaultLineCounter>();
            builder.Services.AddScoped<ILineCounter, CSharpLineCounter>();

            // Register file filtering services
            builder.Services.AddScoped<PoRepoLineTracker.Infrastructure.FileFilters.IFileIgnoreFilter, PoRepoLineTracker.Infrastructure.FileFilters.FileIgnoreFilter>();

// Register failed operation service
            builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IFailedOperationService, PoRepoLineTracker.Infrastructure.Services.FailedOperationService>();
            builder.Services.AddHostedService<PoRepoLineTracker.Infrastructure.Services.FailedOperationBackgroundService>();

builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IGitHubService>(serviceProvider =>
            {
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient("GitHubClient");
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var logger = serviceProvider.GetRequiredService<ILogger<PoRepoLineTracker.Infrastructure.Services.GitHubService>>();
                var gitClient = serviceProvider.GetRequiredService<PoRepoLineTracker.Infrastructure.Interfaces.IGitClient>(); // Get IGitClient
                var lineCounters = serviceProvider.GetServices<ILineCounter>(); // Get all ILineCounter implementations
                var fileIgnoreFilter = serviceProvider.GetRequiredService<PoRepoLineTracker.Infrastructure.FileFilters.IFileIgnoreFilter>(); // Get file ignore filter
                return new PoRepoLineTracker.Infrastructure.Services.GitHubService(httpClient, configuration, logger, lineCounters, gitClient, fileIgnoreFilter); // Inject all dependencies
            });
            builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryDataService, PoRepoLineTracker.Infrastructure.Services.RepositoryDataService>();
            builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IRepositoryService, PoRepoLineTracker.Application.Services.RepositoryService>(); // Re-enabled: RepositoryService now uses MediatR

            // Add MediatR
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand).Assembly));

            var app = builder.Build();
            app.UseMiddleware<ExceptionHandlingMiddleware>(); // Global exception handling

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PoRepoLineTracker API v1");
                    c.RoutePrefix = "swagger";
                });
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");

            // API Endpoints
            app.MapPost("/api/repositories", async (GitHubRepository newRepo, IMediator mediator) =>
            {
                var repo = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand(newRepo.Owner, newRepo.Name, newRepo.CloneUrl));
                return Results.Created($"/api/repositories/{repo.Id}", repo);
            })
            .WithName("AddRepository");

            app.MapGet("/api/repositories", async (IMediator mediator) =>
            {
                var repositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesQuery());
                return Results.Ok(repositories);
            })
            .WithName("GetAllRepositories");

            app.MapGet("/api/repositories/{repositoryId}/linecounts", async (Guid repositoryId, IMediator mediator) =>
            {
                var lineCounts = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetLineCountsForRepositoryQuery(repositoryId));
                return Results.Ok(lineCounts);
            })
            .WithName("GetRepositoryLineCounts");

            app.MapGet("/api/repositories/{repositoryId}/linehistory/{days}", async (Guid repositoryId, int days, IMediator mediator) =>
            {
                try
                {
                    var lineHistory = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetLineCountHistoryQuery(repositoryId, days));
                    return Results.Ok(lineHistory);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving line count history for repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error retrieving line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetRepositoryLineHistory");

            app.MapGet("/api/repositories/allcharts/{days}", async (int days, IMediator mediator) =>
            {
                try
                {
                    var allChartsData = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesLineCountHistoryQuery(days));
                    return Results.Ok(allChartsData);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving line count history for all repositories");
                    return Results.Problem($"Error retrieving all repositories line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetAllRepositoriesLineHistory");

            app.MapGet("/api/settings/file-extensions", async (IMediator mediator) =>
            {
                try
                {
                    var extensions = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetConfiguredFileExtensionsQuery());
                    return Results.Ok(extensions);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving configured file extensions.");
                    return Results.Problem($"Error retrieving configured file extensions: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetConfiguredFileExtensions");

            app.MapGet("/api/repositories/{repositoryId}/file-extension-percentages", async (Guid repositoryId, IMediator mediator) =>
            {
                try
                {
                    var percentages = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetFileExtensionPercentagesQuery(repositoryId));
                    return Results.Ok(percentages);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving file extension percentages for repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error retrieving file extension percentages: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetFileExtensionPercentages");

            app.MapDelete("/api/repositories/{repositoryId}", async (Guid repositoryId, IMediator mediator) =>
            {
                try
                {
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.DeleteRepositoryCommand(repositoryId));
                    Log.Information("Repository {RepositoryId} deleted successfully via API.", repositoryId);
                    return Results.NoContent();
                }
                catch (InvalidOperationException)
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

            app.MapDelete("/api/repositories/all", async (IMediator mediator) =>
            {
                try
                {
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.RemoveAllRepositoriesCommand());
                    Log.Information("All repositories removed successfully via API.");
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error removing all repositories");
                    return Results.Problem($"Error removing all repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("RemoveAllRepositories");

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

            app.MapPost("/api/repositories/bulk", async ([FromBody] IEnumerable<PoRepoLineTracker.Application.Models.BulkRepositoryDto> repositories, IMediator mediator) =>
            {
                try
                {
                    Log.Information("Adding {Count} repositories via bulk endpoint.", repositories?.Count() ?? 0);

                    var addedRepositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddMultipleRepositoriesCommand(repositories ?? Enumerable.Empty<PoRepoLineTracker.Application.Models.BulkRepositoryDto>()));
                    return Results.Ok(addedRepositories);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error adding multiple repositories");
                    return Results.Problem($"Error adding repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("AddMultipleRepositories");

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

            app.MapPost("/api/repositories/{repositoryId}/analyze", async (Guid repositoryId, IMediator mediator) =>
            {
                try
                {
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(repositoryId));
                    return Results.Accepted();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error analyzing repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error analyzing repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("AnalyzeRepository");

app.MapPost("/api/repositories/{repositoryId}/reanalyze", async (Guid repositoryId, IMediator mediator) =>
            {
                try
                {
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(repositoryId, ForceReanalysis: true));
                    return Results.Accepted();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error force re-analyzing repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error force re-analyzing repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("ForceReanalyzeRepository");

            // Failed Operations Endpoints
            app.MapGet("/api/failed-operations/{repositoryId}", async (Guid repositoryId, IFailedOperationService failedOperationService) =>
            {
                try
                {
                    var failedOperations = await failedOperationService.GetFailedOperationsByRepositoryIdAsync(repositoryId);
                    return Results.Ok(failedOperations);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving failed operations for repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error retrieving failed operations: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetFailedOperationsByRepository")
            .RequireAuthorization(); // Require authorization for failed operations endpoints

            app.MapDelete("/api/failed-operations/{failedOperationId}", async (Guid failedOperationId, IFailedOperationService failedOperationService) =>
            {
                try
                {
                    await failedOperationService.DeleteFailedOperationAsync(failedOperationId);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error deleting failed operation {FailedOperationId}", failedOperationId);
                    return Results.Problem($"Error deleting failed operation: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("DeleteFailedOperation")
            .RequireAuthorization(); // Require authorization for failed operations endpoints

            // Health Check Endpoint for Diag.razor page
            app.MapGet("/healthz", async (IRepositoryDataService repoDataService, IGitHubService githubService) =>
            {
                var checks = new List<object>();
                var isHealthy = true;

                try
                {
                    await repoDataService.CheckConnectionAsync();
                    checks.Add(new { Name = "Azure Table Storage", Status = "Healthy" });
                }
                catch (Exception ex)
                {
                    checks.Add(new { Name = "Azure Table Storage", Status = $"Unhealthy: {ex.Message}" });
                    isHealthy = false;
                }

                try
                {
                    await githubService.CheckConnectionAsync();
                    checks.Add(new { Name = "GitHub API", Status = "Healthy" });
                }
                catch (Exception ex)
                {
                    checks.Add(new { Name = "GitHub API", Status = $"Unhealthy: {ex.Message}" });
                    isHealthy = false;
                }

                var healthStatus = new
                {
                    Status = isHealthy ? "Healthy" : "Unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Checks = checks.ToArray()
                };

                return Results.Json(healthStatus);
            })
            .WithName("HealthCheck")
            .WithOpenApi();

            return app;
        }
    }
}
