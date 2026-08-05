using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.GitHub;
using Serilog;

namespace PoRepoLineTracker.API.Features.Auth;

internal static class AuthEndpoints
{
    internal static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Rule 3.1 — the whole /auth slice is anonymous by construction: these are the routes a
        // signed-out browser must reach to sign in, so the group opts out of the FallbackPolicy once.
        var auth = endpoints.MapGroup("/auth")
            .WithTags("Auth")
            .AllowAnonymous();

        auth.MapGet("/login", (string? returnUrl, IConfiguration config) =>
        {
            var ghClientId = config[ConfigKeys.GitHub.ClientId];

            // GitHub is the only provider. If it is not configured, return 503 so the client can
            // show a helpful message instead of a generic 500 "No authentication handler is
            // registered".
            if (!string.IsNullOrEmpty(ghClientId))
            {
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                    [GitHubAuthenticationDefaults.AuthenticationScheme]);
            }

            return Results.Problem(
                title: "No OAuth provider configured",
                detail: "GitHub OAuth is not configured. Set GitHub:ClientId and GitHub:ClientSecret in configuration (user-secrets, appsettings.Development.local.json, or Key Vault).",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("Login");

        // The /auth/login/microsoft route is gone with the Microsoft provider — see
        // AuthServiceExtensions for why. Left as a comment rather than a 410 handler because
        // nothing links to it: the login page's second button was removed in the same change.

        auth.MapGet("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // Redirect to /login so the user lands on the unauthenticated landing
            // page even if their cookie had already expired (avoids a stale-session
            // 302 loop where the auth filter keeps bouncing them).
            return Results.Redirect("/login");
        })
        .WithName("Logout");

        // Anonymous by design: the Blazor client polls this to discover whether it has a
        // session, and must get a well-formed "not authenticated" answer rather than a 401.
        auth.MapGet("/me", async (HttpContext context, IUserService userService) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new AuthResponse(IsAuthenticated: false));

            if (!context.User.TryGetUserId(out var userId))
                return Results.Ok(new AuthResponse(IsAuthenticated: false));

            try
            {
                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    // User not yet persisted (e.g. storage was unavailable during the OAuth
                    // callback). Fall back to claims so the session stays authenticated.
                    Log.Debug("User {UserId} not found in storage; falling back to claims", userId);
                    return Results.Ok(new AuthResponse(
                        IsAuthenticated: true,
                        UserId: userId.ToString(),
                        Username: context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "User",
                        DisplayName: context.User.FindFirst("DisplayName")?.Value,
                        Email: context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                        AvatarUrl: context.User.FindFirst("AvatarUrl")?.Value ?? ""));
                }

                return Results.Ok(new AuthResponse(
                    IsAuthenticated: true,
                    UserId: user.Id.ToString(),
                    Username: user.Username,
                    DisplayName: user.DisplayName,
                    Email: user.Email,
                    AvatarUrl: user.AvatarUrl));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "User service unavailable in GetCurrentUser, using claims fallback");
                return Results.Ok(new AuthResponse(
                    IsAuthenticated: true,
                    UserId: userId.ToString(),
                    Username: context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "User",
                    DisplayName: context.User.FindFirst("DisplayName")?.Value,
                    Email: context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                    AvatarUrl: context.User.FindFirst("AvatarUrl")?.Value ?? ""));
            }
        })
        .WithName("GetCurrentUser");
    }
}
