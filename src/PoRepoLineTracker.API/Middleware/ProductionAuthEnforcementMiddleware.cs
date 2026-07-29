using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PoRepoLineTracker.API.Middleware;

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
/// - /auth/* (login/logout endpoints)
/// - /api/feature-flags (anonymous — UI calls this on every page load
///   to decide which login button and badges to render)
/// - /login (Blazor login page)
/// - /_framework/* (Blazor WASM framework files)
/// - /css/*, /favicon.png, /icon-192.png, /manifest.json (static assets)
/// </summary>
public class ProductionAuthEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProductionAuthEnforcementMiddleware> _logger;

    // Paths that are always accessible without auth (even in production).
    // Mirrors the endpoints marked .AllowAnonymous() in the API — keeping the two
    // lists in sync is the canonical way to make the Blazor UI render in production
    // (the layout calls /api/feature-flags on every page load, before login).
    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/auth/login",
        "/auth/logout",
        "/auth/me",
        // Anonymous endpoint the Blazor UI calls on every render:
        //   /api/feature-flags  → tells the UI which buttons / badges to show
        "/api/feature-flags",
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
        var path = context.Request.Path;
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
            if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsPublicPath(PathString path)
    {
        // Segment-aware match: "/login" matches "/login" and "/login/x" but NOT "/loginfoo".
        // Using raw string StartsWith here would let "/loginfoo" or "/cssfoo" bypass auth.
        foreach (var publicPath in PublicPaths)
        {
            if (path.StartsWithSegments(publicPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
