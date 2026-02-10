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
using Scalar.AspNetCore;
using System.Collections.Generic; // Add this for List<object>
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using PoRepoLineTracker.Api.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Azure.Identity;

namespace PoRepoLineTracker.Api
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            // Initial logger before configuration is loaded
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

            // Add Azure Key Vault configuration provider (secrets from PoShared Key Vault)
            // In production the managed identity is used; locally DefaultAzureCredential
            // falls through to Azure CLI / Visual Studio credentials.
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

            // Register Azure Table Storage client
            // Uses DefaultAzureCredential for cloud endpoints; falls back to connection string for Azurite/emulator
            builder.Services.AddSingleton<Azure.Data.Tables.TableServiceClient>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var connStr = config["AzureTableStorage:ConnectionString"]
                           ?? config["ConnectionStrings:tables"];

                // Azurite / emulator uses a connection string
                if (!string.IsNullOrEmpty(connStr) && connStr.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
                {
                    return new Azure.Data.Tables.TableServiceClient(connStr);
                }

                // Cloud: use the storage account endpoint + DefaultAzureCredential (no keys)
                var storageEndpoint = config["AzureTableStorage:ServiceUrl"];
                if (!string.IsNullOrEmpty(storageEndpoint))
                {
                    return new Azure.Data.Tables.TableServiceClient(
                        new Uri(storageEndpoint),
                        new DefaultAzureCredential());
                }

                // Fallback: plain connection string (for integration tests or legacy config)
                if (!string.IsNullOrEmpty(connStr))
                {
                    return new Azure.Data.Tables.TableServiceClient(connStr);
                }

                return new Azure.Data.Tables.TableServiceClient("UseDevelopmentStorage=true");
            });

            // Add local development configuration
            if (builder.Environment.IsDevelopment())
            {
                builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
            }

            // Configure Serilog with Application Insights
            builder.Host.UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "PoRepoLineTracker")
                    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                    .MinimumLevel.Information();

                // Add file sink only in Development
                if (context.HostingEnvironment.IsDevelopment())
                {
                    configuration.WriteTo.File(
                        "log.txt",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        shared: true,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
                }

                // Add Application Insights sink if connection string is available
                var appInsightsConnectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
                if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
                {
                    configuration.WriteTo.ApplicationInsights(
                        appInsightsConnectionString,
                        TelemetryConverter.Traces);
                }
            });

            // Add OpenAPI services
            builder.Services.AddOpenApi(options =>
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info.Title = "PoRepoLineTracker API";
                    document.Info.Version = "v1";
                    document.Info.Description = "API for tracking repository line counts and code statistics";
                    return Task.CompletedTask;
                });
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

            // Register User Service for authentication
            builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IUserService, PoRepoLineTracker.Infrastructure.Services.UserService>();

            // Register User Preferences Service
            builder.Services.AddScoped<PoRepoLineTracker.Application.Interfaces.IUserPreferencesService, PoRepoLineTracker.Infrastructure.Services.UserPreferencesService>();

            // Add MediatR
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand).Assembly));

            // Configure Data Protection for cookie encryption (filesystem-based key storage)
            builder.Services.AddDataProtection()
                .SetApplicationName("PoRepoLineTracker")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                    builder.Environment.ContentRootPath, "..", "dataprotection-keys")));

            // Configure GitHub OAuth Authentication
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = GitHubAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "PoRepoLineTracker.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Always use Secure in production
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.Events.OnRedirectToLogin = context =>
                {
                    // For API requests, return 401 instead of redirect
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddGitHub(options =>
            {
                options.ClientId = builder.Configuration["GitHub:ClientId"] ?? throw new InvalidOperationException("GitHub:ClientId is not configured");
                options.ClientSecret = builder.Configuration["GitHub:ClientSecret"] ?? throw new InvalidOperationException("GitHub:ClientSecret is not configured");
                options.CallbackPath = builder.Configuration["GitHub:CallbackPath"] ?? "/signin-github";
                
                // Configure correlation cookie for OAuth state validation
                // Use Lax since both initial request and callback are on the same domain
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.HttpOnly = true;
                
                // Request scopes for repository access
                options.Scope.Add("user:email");
                options.Scope.Add("read:user");
                options.Scope.Add("repo"); // Full repo access for private repos
                
                options.SaveTokens = true;
                
                // Handle remote authentication failures gracefully
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/?error=auth_failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
                
                options.Events.OnCreatingTicket = async context =>
                {
                    // Extract user info from GitHub claims
                    var gitHubId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value; // GitHub maps "login" to ClaimTypes.Name
                    var displayName = context.Principal?.FindFirst(GitHubAuthenticationConstants.Claims.Name)?.Value
                                   ?? context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value;
                    var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                    
                    // Avatar URL comes from the user JSON response directly
                    var avatarUrl = context.User.GetProperty("avatar_url").GetString();
                    var accessToken = context.AccessToken;

                    if (gitHubId != null && username != null && accessToken != null)
                    {
                        // Get or create user in our system
                        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                        var user = new User
                        {
                            GitHubId = gitHubId,
                            Username = username,
                            DisplayName = displayName ?? username,
                            Email = email,
                            AvatarUrl = avatarUrl ?? string.Empty,
                            AccessToken = accessToken
                        };

                        var savedUser = await userService.UpsertUserAsync(user);

                        // Add our internal user ID to claims
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        identity?.AddClaim(new Claim("UserId", savedUser.Id.ToString()));
                    }
                };
            });

            builder.Services.AddAuthorization();

            // Add Application Insights telemetry
            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            });

            // Add OpenTelemetry for distributed tracing and custom metrics
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: AppTelemetry.SourceName,
                        serviceVersion: AppTelemetry.Version))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(AppTelemetry.SourceName) // Add custom ActivitySource from API layer
                        .AddSource(PoRepoLineTracker.Application.Telemetry.AppTelemetry.SourceName) // Add Application layer traces
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            // Enrich traces with additional HTTP request details
                            options.RecordException = true;
                            options.Filter = context =>
                            {
                                // Skip health check endpoint from traces
                                return !context.Request.Path.StartsWithSegments("/health");
                            };
                        })
                        .AddHttpClientInstrumentation(); // Track outgoing HTTP calls (e.g., to GitHub API)

                    // Export traces to console in development only when explicitly enabled
                    // Set EnableConsoleExporters=true in configuration to enable
                    if (builder.Environment.IsDevelopment() && string.Equals(builder.Configuration["EnableConsoleExporters"], "true", StringComparison.OrdinalIgnoreCase))
                    {
                        tracing.AddConsoleExporter();
                    }

                    // Export traces to OTLP (Application Insights) in production
                    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        tracing.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                        });
                    }
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddMeter(AppTelemetry.SourceName) // Add custom Meter from API layer
                        .AddMeter(PoRepoLineTracker.Application.Telemetry.AppTelemetry.SourceName) // Add Application layer metrics
                        .AddAspNetCoreInstrumentation() // Add ASP.NET Core metrics
                        .AddHttpClientInstrumentation(); // Add HttpClient metrics

                    // Export metrics to console in development only when explicitly enabled
                    // Set EnableConsoleExporters=true in configuration to enable
                    if (builder.Environment.IsDevelopment() && string.Equals(builder.Configuration["EnableConsoleExporters"], "true", StringComparison.OrdinalIgnoreCase))
                    {
                        metrics.AddConsoleExporter();
                    }

                    // Export metrics to OTLP (Application Insights) in production
                    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        metrics.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                        });
                    }
                });

            // Add Health Checks
            builder.Services.AddHealthChecks()
                .AddCheck<PoRepoLineTracker.Api.HealthChecks.AzureTableStorageHealthCheck>("azure_table_storage");

            // Configure forwarded headers for reverse proxy (Azure Container Apps)
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // Clear known networks/proxies to trust all forwarders (Azure Container Apps)
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            var app = builder.Build();

            // Apply forwarded headers first (before any other middleware)
            app.UseForwardedHeaders();

            app.UseMiddleware<ExceptionHandlingMiddleware>(); // Global exception handling

            // Configure the HTTP request pipeline.
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

            // Authentication & Authorization middleware (after HTTPS redirect, before endpoints)
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");

            // Deprecated: detailed framework health endpoint removed. Use /health for JSON external checks instead.

            // ========== AUTHENTICATION ENDPOINTS ==========
            
            // Login - redirects to GitHub OAuth
            app.MapGet("/api/auth/login", (string? returnUrl) =>
            {
                var properties = new AuthenticationProperties
                {
                    RedirectUri = returnUrl ?? "/"
                };
                return Results.Challenge(properties, [GitHubAuthenticationDefaults.AuthenticationScheme]);
            })
            .WithName("Login")
            .AllowAnonymous();

            // Logout
            app.MapGet("/api/auth/logout", async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/");
            })
            .WithName("Logout");

            // Get current user info
            app.MapGet("/api/auth/me", async (HttpContext context, IUserService userService) =>
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    return Results.Ok(new { isAuthenticated = false });
                }

                var userIdClaim = context.User.FindFirst("UserId")?.Value;
                if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Ok(new { isAuthenticated = false });
                }

                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return Results.Ok(new { isAuthenticated = false });
                }

                return Results.Ok(new
                {
                    isAuthenticated = true,
                    userId = user.Id,
                    username = user.Username,
                    displayName = user.DisplayName,
                    avatarUrl = user.AvatarUrl,
                    email = user.Email
                });
            })
            .WithName("GetCurrentUser")
            .AllowAnonymous();

            // ========== API ENDPOINTS ==========
            
            // API Endpoints
            app.MapPost("/api/repositories", async (GitHubRepository newRepo, IMediator mediator) =>
            {
                var repo = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddRepositoryCommand(newRepo.Owner, newRepo.Name, newRepo.CloneUrl));
                return Results.Created($"/api/repositories/{repo.Id}", repo);
            })
            .WithName("AddRepository");

            app.MapGet("/api/repositories", async (HttpContext httpContext, IMediator mediator) =>
            {
                // Get the current user's ID from claims
                var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                var repositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesQuery(userId));
                return Results.Ok(repositories);
            })
            .RequireAuthorization()
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

            app.MapGet("/api/repositories/allcharts/{days}", async (int days, HttpContext httpContext, IMediator mediator) =>
            {
                try
                {
                    // Get the current user's ID from claims
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var allChartsData = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetAllRepositoriesLineCountHistoryQuery(days, userId));
                    return Results.Ok(allChartsData);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving line count history for all repositories");
                    return Results.Problem($"Error retrieving all repositories line count history: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
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

            app.MapGet("/api/settings/chart/max-lines", (IConfiguration configuration) =>
            {
                var maxLines = configuration.GetValue<int>("ChartSettings:MaxLinesOfCode", 50000);
                return Results.Ok(maxLines);
            })
            .WithName("GetChartMaxLines");

            // User Preferences Endpoints
            app.MapGet("/api/settings/user-preferences", async (HttpContext httpContext, PoRepoLineTracker.Application.Interfaces.IUserPreferencesService preferencesService) =>
            {
                try
                {
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var preferences = await preferencesService.GetPreferencesAsync(userId);
                    return Results.Ok(preferences);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving user preferences");
                    return Results.Problem($"Error retrieving user preferences: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("GetUserPreferences");

            app.MapPut("/api/settings/user-preferences", async (HttpContext httpContext, PoRepoLineTracker.Application.Interfaces.IUserPreferencesService preferencesService, PoRepoLineTracker.Domain.Models.UserPreferences preferences) =>
            {
                try
                {
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    // Ensure the UserId in preferences matches the authenticated user
                    preferences = preferences with { UserId = userId, LastUpdated = DateTime.UtcNow };
                    await preferencesService.SavePreferencesAsync(preferences);
                    return Results.Ok(preferences);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error saving user preferences");
                    return Results.Problem($"Error saving user preferences: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("SaveUserPreferences");

            app.MapGet("/api/settings/user-extensions", async (HttpContext httpContext, PoRepoLineTracker.Application.Interfaces.IUserPreferencesService preferencesService) =>
            {
                try
                {
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var extensions = await preferencesService.GetFileExtensionsAsync(userId);
                    return Results.Ok(extensions);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving user file extensions");
                    return Results.Problem($"Error retrieving user file extensions: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("GetUserFileExtensions");

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

            app.MapGet("/api/repositories/{repositoryId}/top-files", async (Guid repositoryId, IMediator mediator, int count = 5) =>
            {
                try
                {
                    var topFiles = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Queries.GetTopFilesQuery(repositoryId, count));
                    return Results.Ok(topFiles);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error retrieving top files for repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error retrieving top files: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("GetTopFiles");

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

            app.MapDelete("/api/repositories/all", async (HttpContext httpContext, IMediator mediator) =>
            {
                try
                {
                    // Get the current user's ID from claims
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        Log.Warning("Remove all repositories failed: No valid UserId claim found");
                        return Results.Unauthorized();
                    }

                    Log.Information("Starting removal of all repositories for user {UserId}", userId);
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.RemoveAllRepositoriesCommand(userId));
                    Log.Information("All repositories for user {UserId} removed successfully via API.", userId);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error removing all repositories: {ErrorType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
                    return Results.Problem($"Error removing all repositories: {ex.GetType().Name} - {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("RemoveAllRepositories");

            app.MapGet("/api/github/user-repositories", async (HttpContext httpContext, IGitHubService githubService, IUserService userService) =>
            {
                try
                {
                    // Get the current user's ID from claims
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    // Get the user's access token
                    var accessToken = await userService.GetAccessTokenAsync(userId);
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        return Results.Unauthorized();
                    }

                    var userRepositories = await githubService.GetUserRepositoriesAsync(accessToken);
                    return Results.Ok(userRepositories);
                }
                catch (InvalidOperationException ex)
                {
                    Log.Warning("Authentication error: {ErrorMessage}", ex.Message);
                    return Results.BadRequest($"Authentication error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error fetching user repositories from GitHub API");
                    return Results.Problem($"Error fetching user repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("GetUserRepositories");

            app.MapPost("/api/repositories/bulk", async ([FromBody] IEnumerable<PoRepoLineTracker.Application.Models.BulkRepositoryDto> repositories, HttpContext httpContext, IMediator mediator) =>
            {
                try
                {
                    // Get the current user's ID from claims
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    Log.Information("=== BULK REPOSITORY ADD ENDPOINT CALLED ===");
                    Log.Information("Request body received: {IsNull}", repositories == null ? "NULL" : "NOT NULL");

                    var repoList = repositories?.ToList() ?? new List<PoRepoLineTracker.Application.Models.BulkRepositoryDto>();
                    Log.Information("Number of repositories in request: {Count}", repoList.Count);

                    // Log each repository in detail
                    for (int i = 0; i < repoList.Count; i++)
                    {
                        var repo = repoList[i];
                        Log.Information("API Request Repo [{Index}]: Owner='{Owner}', RepoName='{RepoName}', CloneUrl='{CloneUrl}'",
                            i, repo?.Owner ?? "NULL", repo?.RepoName ?? "NULL", repo?.CloneUrl ?? "NULL");
                    }

                    Log.Information("Sending AddMultipleRepositoriesCommand to MediatR with {Count} repositories for user {UserId}", repoList.Count, userId);
                    var addedRepositories = await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AddMultipleRepositoriesCommand(repositories ?? Enumerable.Empty<PoRepoLineTracker.Application.Models.BulkRepositoryDto>(), userId));

                    var addedList = addedRepositories.ToList();
                    Log.Information("MediatR returned {Count} repositories", addedList.Count);
                    Log.Information("Repositories returned: {Repos}", string.Join(", ", addedList.Select(r => $"{r.Owner}/{r.Name}")));

                    return Results.Ok(addedRepositories);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "EXCEPTION in bulk repository endpoint: {Message}. Stack: {StackTrace}", ex.Message, ex.StackTrace);
                    return Results.Problem($"Error adding repositories: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("AddMultipleRepositories");

            // Individual health endpoints removed. Use the aggregated `/health` endpoint to check external dependencies and receive a JSON report.

            // POST to create a new analysis (force=true to re-analyze)
            app.MapPost("/api/repositories/{repositoryId}/analyses", async (Guid repositoryId, [FromQuery] bool force, IMediator mediator) =>
            {
                try
                {
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(repositoryId, ForceReanalysis: force));
                    Log.Information("Analysis initiated for repository {RepositoryId} (force={Force})", repositoryId, force);
                    return Results.Accepted();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error analyzing repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error analyzing repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .WithName("CreateRepositoryAnalysis");

            // POST to re-analyze repository from scratch (clears all existing data and re-analyzes with current user's file extension preferences)
            app.MapPost("/api/repositories/{repositoryId}/reanalyze", async (Guid repositoryId, HttpContext httpContext, IMediator mediator) =>
            {
                try
                {
                    var userIdClaim = httpContext.User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    Log.Information("Re-analysis requested for repository {RepositoryId} by user {UserId} - clearing existing data", repositoryId, userId);
                    await mediator.Send(new PoRepoLineTracker.Application.Features.Repositories.Commands.AnalyzeRepositoryCommitsCommand(
                        repositoryId, 
                        ForceReanalysis: false, 
                        ClearExistingData: true));
                    
                    return Results.Accepted(value: new { message = "Re-analysis started. All commit data will be re-calculated with your current file extension preferences." });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error re-analyzing repository {RepositoryId}", repositoryId);
                    return Results.Problem($"Error re-analyzing repository: {ex.Message}", statusCode: (int)HttpStatusCode.InternalServerError);
                }
            })
            .RequireAuthorization()
            .WithName("ReanalyzeRepository");

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

            // Health Check Endpoint (checks external connections and returns JSON)
            app.MapGet("/health", async (IRepositoryDataService repoDataService, IGitHubService githubService) =>
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
            .WithName("HealthCheckSimple");

            // Diagnostics endpoint — exposes sanitised config values for troubleshooting
            // Middle characters of each value are masked for security (e.g., "abc***xyz")
            app.MapGet("/diag", (IConfiguration configuration, IWebHostEnvironment env) =>
            {
                static string Mask(string? value)
                {
                    if (string.IsNullOrEmpty(value)) return "(not set)";
                    if (value.Length <= 6) return new string('*', value.Length);
                    var visibleLen = Math.Min(3, value.Length / 4);
                    return string.Concat(value.AsSpan(0, visibleLen), "***", value.AsSpan(value.Length - visibleLen));
                }

                var diag = new
                {
                    Environment = env.EnvironmentName,
                    Timestamp = DateTime.UtcNow,
                    KeyVault = new
                    {
                        Url = configuration["KeyVault:Url"] ?? "(not set)"
                    },
                    AzureTableStorage = new
                    {
                        ServiceUrl = Mask(configuration["AzureTableStorage:ServiceUrl"]),
                        ConnectionString = Mask(configuration["AzureTableStorage:ConnectionString"])
                    },
                    GitHub = new
                    {
                        ClientId = Mask(configuration["GitHub:ClientId"]),
                        ClientSecret = Mask(configuration["GitHub:ClientSecret"]),
                        PAT = Mask(configuration["GitHub:PAT"]),
                        CallbackPath = configuration["GitHub:CallbackPath"]
                    },
                    ApplicationInsights = new
                    {
                        ConnectionString = Mask(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"])
                    },
                    OpenTelemetry = new
                    {
                        OtlpEndpoint = Mask(configuration["OpenTelemetry:OtlpEndpoint"])
                    }
                };

                return Results.Json(diag);
            })
            .WithName("Diagnostics")
            .AllowAnonymous();

            // Client Logging Endpoint (Development only for security)
            if (app.Environment.IsDevelopment())
            {
                app.MapPost("/api/log/client", ([FromBody] ClientLogEntry logEntry, ILogger<Program> logger) =>
                {
                    // Log client-side events to server-side logging infrastructure
                    var logMessage = $"[CLIENT] {logEntry.Message}";

                    switch (logEntry.Level.ToUpperInvariant())
                    {
                        case "ERROR":
                        case "FATAL":
                            logger.LogError(logEntry.Exception, logMessage, logEntry.Properties);
                            break;
                        case "WARNING":
                        case "WARN":
                            logger.LogWarning(logMessage, logEntry.Properties);
                            break;
                        case "INFO":
                        case "INFORMATION":
                            logger.LogInformation(logMessage, logEntry.Properties);
                            break;
                        case "DEBUG":
                            logger.LogDebug(logMessage, logEntry.Properties);
                            break;
                        default:
                            logger.LogInformation(logMessage, logEntry.Properties);
                            break;
                    }

                    return Results.Ok(new { Status = "Logged" });
                })
                .WithName("LogClientEvent")
                .WithSummary("Accepts client-side log entries (Development only)")
                .WithDescription("Ingests client-side logs and forwards them to server-side logging infrastructure including Application Insights");
            }

            return app;
        }

        // Client log entry model
        public record ClientLogEntry(
            string Level,
            string Message,
            string? Exception = null,
            Dictionary<string, object>? Properties = null
        );
    }
}
