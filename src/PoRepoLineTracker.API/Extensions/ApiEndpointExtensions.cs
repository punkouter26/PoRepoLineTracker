using PoRepoLineTracker.API.Features.AiDetection;
using PoRepoLineTracker.API.Features.Auth;
using PoRepoLineTracker.API.Features.Diagnostics;
using PoRepoLineTracker.API.Features.GitHub;
using PoRepoLineTracker.API.Features.Repositories;
using PoRepoLineTracker.API.Features.Settings;

namespace PoRepoLineTracker.API.Extensions;

/// <summary>
/// GoF Facade Pattern: Provides a unified interface to register all API endpoint groups.
/// Each endpoint group (Auth, Repositories, GitHub, Settings, Diagnostics)
/// lives in its own file following the VSA (Vertical Slice Architecture) principle.
///
/// SOLID — Interface Segregation Principle: Each endpoint module has its own mapping method,
/// keeping the API surface organized and testable.
///
/// Rule 3.1: every module maps onto an <see cref="IEndpointRouteBuilder"/> and builds its
/// routes under a <c>MapGroup()</c>, so the route prefix, tags, and authorization policy for a
/// slice are declared once instead of repeated on every endpoint.
/// </summary>
public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        app.MapAuthEndpoints();
        app.MapRepositoryEndpoints(isDevelopment);
        app.MapSettingsEndpoints();
        app.MapGitHubEndpoints();
        app.MapDiagnosticsEndpoints();
        app.MapUploadEndpoints();
        app.MapAiDetectionEndpoints();

        if (isDevelopment)
        {
            app.MapDevOnlyEndpoints();
        }

        return app;
    }
}
