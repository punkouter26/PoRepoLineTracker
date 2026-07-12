namespace PoRepoLineTracker.Api.Extensions;

/// <summary>
/// GoF Facade Pattern: Provides a unified interface to register all API endpoint groups.
/// Each endpoint group (Auth, Repositories, GitHub, Settings, Diagnostics)
/// lives in its own file following the VSA (Vertical Slice Architecture) principle.
/// 
/// SOLID — Interface Segregation Principle: Each endpoint module has its own mapping method,
/// keeping the API surface organized and testable.
/// </summary>
public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapRepositoryEndpoints();
        app.MapSettingsEndpoints();
        app.MapGitHubEndpoints();
        app.MapDiagnosticsEndpoints();
        app.MapUploadEndpoints();
        app.MapAiDetectionEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevOnlyEndpoints();
        }

        return app;
    }
}
