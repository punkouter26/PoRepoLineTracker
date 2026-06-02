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
        app.MapGet("/api/auth/login", (string? returnUrl, IConfiguration config) =>
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

        // GUEST login — dev-only. Creates a session with username "GUEST{random 8 digits}".
        // The button is programmatically hidden in production; this server-side guard prevents
        // direct URL access.
        // SOLID — OCP: adds a new auth path without modifying existing OAuth providers.
        app.MapGet("/api/auth/login-guest", (IWebHostEnvironment env) =>
        {
            if (!env.IsDevelopment())
                return Results.Forbid();

            var randomSuffix = Random.Shared.Next(10000000, 99999999).ToString();
            var guestUsername = $"GUEST{randomSuffix}";

            var claims = new List<Claim>
            {
                new("UserId", Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, guestUsername),
                new("DisplayName", guestUsername),
                new("IsAnon", "true")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            return Results.SignIn(principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
                },
                CookieAuthenticationDefaults.AuthenticationScheme);
        })
        .WithName("LoginGuest")
        .AllowAnonymous();

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
