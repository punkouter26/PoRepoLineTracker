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
        app.MapGet("/api/auth/login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                [GitHubAuthenticationDefaults.AuthenticationScheme]))
            .WithName("Login")
            .AllowAnonymous();

        // Microsoft OAuth login — challenges Microsoft account provider
        // "Microsoft" is MicrosoftAccountDefaults.AuthenticationScheme (scheme name string)
        app.MapGet("/api/auth/login-microsoft", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                ["Microsoft"]))
            .WithName("LoginMicrosoft")
            .AllowAnonymous();

        app.MapGet("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        })
        .WithName("Logout");

        app.MapGet("/api/auth/me", async (HttpContext context, IUserService userService) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new AuthResponse(IsAuthenticated: false));

            var userIdClaim = context.User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Ok(new AuthResponse(IsAuthenticated: false));

            // Detect ANON logins created by the dev bypass — these have an "IsAnon" claim set to "true"
            var isAnon = string.Equals(
                context.User.FindFirst("IsAnon")?.Value, "true",
                StringComparison.OrdinalIgnoreCase);

            try
            {
                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    // ANON users and dev-bypass users are not persisted in the database.
                    // Fall back to claims so the session stays authenticated.
                    Log.Debug("User {UserId} not found in storage; falling back to claims (IsAnon={IsAnon})", userId, isAnon);
                    return Results.Ok(new AuthResponse(
                        IsAuthenticated: true,
                        UserId: userId.ToString(),
                        Username: context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "User",
                        DisplayName: context.User.FindFirst("DisplayName")?.Value,
                        Email: context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                        AvatarUrl: context.User.FindFirst("AvatarUrl")?.Value ?? "",
                        IsAnon: isAnon));
                }

                return Results.Ok(new AuthResponse(
                    IsAuthenticated: true,
                    UserId: user.Id.ToString(),
                    Username: user.Username,
                    DisplayName: user.DisplayName,
                    Email: user.Email,
                    AvatarUrl: user.AvatarUrl,
                    IsAnon: isAnon));
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
                    AvatarUrl: context.User.FindFirst("AvatarUrl")?.Value ?? "",
                    IsAnon: isAnon));
            }
        })
        .WithName("GetCurrentUser")
        .AllowAnonymous();
    }
}
