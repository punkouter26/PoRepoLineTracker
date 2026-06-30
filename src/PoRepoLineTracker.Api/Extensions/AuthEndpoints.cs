using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.GitHub;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models;
using Serilog;

namespace PoRepoLineTracker.Api.Extensions;

internal static class AuthEndpoints
{
    internal static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/login", (string? returnUrl, IConfiguration config) =>
        {
            var ghClientId = config["GitHub:ClientId"];
            var msClientId = config["Microsoft:ClientId"];
            var msClientSecret = config["Microsoft:ClientSecret"];

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
        .WithName("Login")
        .AllowAnonymous();

        // Microsoft OAuth login — challenges Microsoft account provider
        // "Microsoft" is MicrosoftAccountDefaults.AuthenticationScheme (scheme name string)
        app.MapGet("/auth/login/microsoft", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                ["Microsoft"]))
            .WithName("LoginMicrosoft")
            .AllowAnonymous();

        app.MapGet("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        })
        .WithName("Logout");

        app.MapGet("/auth/me", async (HttpContext context, IUserService userService) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new AuthResponse(IsAuthenticated: false));

            var userIdClaim = context.User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
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
        .WithName("GetCurrentUser")
        .AllowAnonymous();
    }
}
