using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.GitHub;
using PoRepoLineTracker.Application.Interfaces;
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

        app.MapGet("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        })
        .WithName("Logout");

        app.MapGet("/api/auth/me", async (HttpContext context, IUserService userService) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new { isAuthenticated = false });

            var userIdClaim = context.User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                return Results.Ok(new { isAuthenticated = false });

            try
            {
                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                    return Results.Ok(new { isAuthenticated = false });

                return Results.Ok(new
                {
                    isAuthenticated = true,
                    userId = user.Id,
                    username = user.Username,
                    displayName = user.DisplayName,
                    avatarUrl = user.AvatarUrl,
                    email = user.Email
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "User service unavailable in GetCurrentUser, using claims fallback");
                return Results.Ok(new
                {
                    isAuthenticated = true,
                    userId = userId.ToString(),
                    username = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "User",
                    displayName = context.User.FindFirst("DisplayName")?.Value,
                    email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                    avatarUrl = context.User.FindFirst("AvatarUrl")?.Value ?? ""
                });
            }
        })
        .WithName("GetCurrentUser")
        .AllowAnonymous();
    }
}
