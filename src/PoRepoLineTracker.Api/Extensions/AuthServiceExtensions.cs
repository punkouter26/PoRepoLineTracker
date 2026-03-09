using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Api.Extensions;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddDataProtection()
            .SetApplicationName("PoRepoLineTracker")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                environment.ContentRootPath, "..", "dataprotection-keys")));

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = GitHubAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "PoRepoLineTracker.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
            options.LoginPath = "/auth/login";
            options.LogoutPath = "/auth/logout";
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        })
        .AddGitHub(options =>
        {
            options.ClientId = configuration["GitHub:ClientId"]
                ?? throw new InvalidOperationException("GitHub:ClientId is not configured");
            options.ClientSecret = configuration["GitHub:ClientSecret"]
                ?? throw new InvalidOperationException("GitHub:ClientSecret is not configured");
            options.CallbackPath = configuration["GitHub:CallbackPath"] ?? "/signin-github";

            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.HttpOnly = true;

            options.Scope.Add("user:email");
            options.Scope.Add("read:user");
            options.Scope.Add("repo");
            options.SaveTokens = true;

            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect("/?error=auth_failed");
                context.HandleResponse();
                return Task.CompletedTask;
            };

            // Return 401 for API fetch calls instead of redirecting to GitHub OAuth,
            // which would cause a browser CORS error on the cross-origin redirect.
            // Exclude /api/auth/login — that endpoint intentionally challenges to GitHub.
            options.Events.OnRedirectToAuthorizationEndpoint = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    && !context.Request.Path.StartsWithSegments("/api/auth/login"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };

            options.Events.OnCreatingTicket = async context =>
            {
                var gitHubId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                var displayName = context.Principal?.FindFirst(GitHubAuthenticationConstants.Claims.Name)?.Value
                               ?? context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value;
                var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                var avatarUrl = context.User.GetProperty("avatar_url").GetString();
                var accessToken = context.AccessToken;

                if (gitHubId != null && username != null && accessToken != null)
                {
                    var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                    var savedUser = await userService.UpsertUserAsync(new User
                    {
                        GitHubId = gitHubId,
                        Username = username,
                        DisplayName = displayName ?? username,
                        Email = email,
                        AvatarUrl = avatarUrl ?? string.Empty,
                        AccessToken = accessToken
                    });
                    (context.Principal?.Identity as ClaimsIdentity)?.AddClaim(
                        new Claim("UserId", savedUser.Id.ToString()));
                }
            };
        });

        services.AddAuthorization();
        return services;
    }
}
