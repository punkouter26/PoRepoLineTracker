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
            var msClientId = config[ConfigKeys.Microsoft.ClientId];
            var msClientSecret = config[ConfigKeys.Microsoft.ClientSecret];

            // Prefer GitHub OAuth when configured; fall back to Microsoft OAuth.
            // If neither is configured, return 503 so the client can show a helpful message
            // instead of a generic 500 "No authentication handler is registered".
            if (!string.IsNullOrEmpty(ghClientId))
            {
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                    [GitHubAuthenticationDefaults.AuthenticationScheme]);
            }
            if (!string.IsNullOrEmpty(msClientId) && !string.IsNullOrEmpty(msClientSecret))
            {
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                    ["Microsoft"]);
            }

            return Results.Problem(
                title: "No OAuth provider configured",
                detail: "Neither GitHub nor Microsoft OAuth is configured. Set GitHub:ClientId (and GitHub:ClientSecret) or Microsoft:ClientId (and Microsoft:ClientSecret) in configuration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("Login");

        // Microsoft OAuth login — challenges Microsoft account provider
        // "Microsoft" is MicrosoftAccountDefaults.AuthenticationScheme (scheme name string)
        auth.MapGet("/login/microsoft", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                ["Microsoft"]))
            .WithName("LoginMicrosoft");

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
