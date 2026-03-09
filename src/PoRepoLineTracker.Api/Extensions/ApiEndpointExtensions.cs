namespace PoRepoLineTracker.Api.Extensions;

/// <summary>
/// Entry point that wires all VSA-grouped endpoint modules.
/// Each group lives in its own file (AuthEndpoints, RepositoryEndpoints, etc.)
/// </summary>
public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapRepositoryEndpoints();
        app.MapSettingsEndpoints();
        app.MapGitHubEndpoints();
        app.MapFailedOperationEndpoints();
        app.MapDiagnosticsEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevOnlyEndpoints();
        }

        return app;
    }
}
