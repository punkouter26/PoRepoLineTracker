using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PoRepoLineTracker.Api.Middleware;

/// <summary>
/// Rule 13 — Production Authentication Enforcement.
/// In Production (non-Development), all unauthenticated requests to non-public
/// endpoints are challenged to Microsoft OAuth. This ensures users must log in
/// via Microsoft OAuth or GitHub OAuth before accessing the app.
///
/// In Development, this middleware is a no-op, allowing GUEST mode and
/// unauthenticated local development.
///
/// Public endpoints that are always accessible:
/// - /health (health checks)
/// - /diag (diagnostics — requires auth internally)
/// - /api/auth/* (login/logout endpoints)
/// - /login (Blazor login page)
/// - /_framework/* (Blazor WASM framework files)
/// - /css/*, /favicon.png, /icon-192.png, /manifest.json (static assets)
/// </summary>
public class ProductionAuthEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProductionAuthEnforcementMiddleware> _logger;

    // Paths that are always accessible without auth (even in production)
    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/api/auth/login",
        "/api/auth/login-microsoft",
        "/api/auth/login-guest",
        "/api/auth/logout",
        "/api/auth/me",
        "/login",
        "/_framework",
        "/css",
        "/favicon.png",
        "/icon-192.png",
        "/manifest.json",
        "/web.config",
    };

    public ProductionAuthEnforcementMiddleware(
        RequestDelegate next,
        IWebHostEnvironment env,
        ILogger<ProductionAuthEnforcementMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // In any non-Production environment (Development, Test), allow everything so GUEST
        // mode, local development and E2E runs work. OAuth is enforced only in Production (Rule 13).
        if (!_env.IsProduction())
        {
            await _next(context);
            return;
        }

        // In Production: check if the path is public
        var path = context.Request.Path.Value ?? "/";
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // In Production: if user is not authenticated, challenge to Microsoft OAuth
        if (context.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogInformation(
                "Production auth enforcement: unauthenticated request to {Path} — challenging to Microsoft OAuth",
                path);

            // For API calls, return 401
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            // For page requests, challenge to Microsoft OAuth
            await context.ChallengeAsync("Microsoft", new AuthenticationProperties
            {
                RedirectUri = path
            });
            return;
        }

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        // Exact match
        if (PublicPaths.Contains(path))
            return true;

        // Prefix match for static assets and framework files
        foreach (var publicPath in PublicPaths)
        {
            if (path.StartsWith(publicPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
