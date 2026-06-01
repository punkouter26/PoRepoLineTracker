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

        // Rule 13 — In Production, default to Microsoft OAuth as the primary challenge.
        // In Development, GitHub OAuth remains the default for backwards compatibility.
        // SOLID — OCP: the production auth enforcement middleware handles the redirect,
        // so this just sets the default challenge scheme.
        var defaultChallengeScheme = environment.IsDevelopment()
            ? GitHubAuthenticationDefaults.AuthenticationScheme
            : "Microsoft";

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = defaultChallengeScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "PoRepoLineTracker.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Use SameAsRequest so cookies work over plain HTTP on localhost
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
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

            // Use SameAsRequest so cookies work over plain HTTP on localhost
            options.CorrelationCookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
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
                    try
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
                    catch (Exception ex)
                    {
                        // Storage not available (e.g. no Azurite locally).
                        // Log the error but don't fail the OAuth flow — user can still
                        // log in with basic claims and a temporary UserId for this session.
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(ex, "Failed to upsert user during GitHub OAuth callback — storage may not be available. User {GitHubId}/{Username} will use in-memory claims.", gitHubId, username);
                        // Add a temporary UserId claim so /api/auth/me recognizes the authenticated user
                        (context.Principal?.Identity as ClaimsIdentity)?.AddClaim(
                            new Claim("UserId", Guid.NewGuid().ToString()));
                    }
                }
            };
        });

        // Microsoft OAuth — available in both dev and prod (personal & work Microsoft accounts).
        // Uses the generic OAuth2 handler pointing at the Microsoft identity platform v2 endpoints.
        // Requires Microsoft:ClientId and Microsoft:ClientSecret in configuration / Key Vault.
        // SOLID — OCP: extending auth without modifying GitHub provider configuration.
        var msClientId = configuration["Microsoft:ClientId"];
        var msClientSecret = configuration["Microsoft:ClientSecret"];
        if (!string.IsNullOrEmpty(msClientId) && !string.IsNullOrEmpty(msClientSecret))
        {
            services.AddAuthentication()
                .AddOAuth("Microsoft", "Microsoft Account", options =>
                {
                    options.ClientId = msClientId;
                    options.ClientSecret = msClientSecret;
                    options.CallbackPath = "/signin-microsoft";
                    options.AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
                    options.TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
                    options.UserInformationEndpoint = "https://graph.microsoft.com/v1.0/me";
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("email");
                    options.Scope.Add("User.Read");
                    options.SaveTokens = true;

                    options.CorrelationCookie.SecurePolicy = environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest
                        : CookieSecurePolicy.Always;
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.HttpOnly = true;

                    options.Events.OnRemoteFailure = context =>
                    {
                        context.Response.Redirect("/?error=ms_auth_failed");
                        context.HandleResponse();
                        return Task.CompletedTask;
                    };

                    // Fetch user info from Microsoft Graph after token exchange, map claims,
                    // then upsert the user in our storage.
                    // SOLID — DIP: resolve IUserService from the DI container at runtime
                    options.Events.OnCreatingTicket = async context =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                        using var httpResponse = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                        httpResponse.EnsureSuccessStatusCode();

                        using var graphDoc = System.Text.Json.JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                        var root = graphDoc.RootElement;

                        var nameId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        var displayName = root.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() : null;
                        var mail = root.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() : null;
                        var upn = root.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;
                        var email = mail ?? upn;

                        // Add standard claims to the identity
                        var identity = (ClaimsIdentity?)context.Principal?.Identity;
                        if (identity != null)
                        {
                            if (nameId != null) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, nameId));
                            if (displayName != null) identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
                            if (email != null) identity.AddClaim(new Claim(ClaimTypes.Email, email));
                        }

                        if (!string.IsNullOrEmpty(nameId))
                        {
                            try
                            {
                                var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                                var savedUser = await userService.UpsertUserAsync(new User
                                {
                                    GitHubId = $"ms:{nameId}",
                                    Username = email ?? displayName ?? nameId,
                                    DisplayName = displayName ?? email ?? nameId,
                                    Email = email,
                                    AvatarUrl = string.Empty,
                                    AccessToken = context.AccessToken ?? string.Empty
                                });
                                identity?.AddClaim(new Claim("UserId", savedUser.Id.ToString()));
                            }
                            catch (Exception ex)
                            {
                                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                                logger.LogWarning(ex, "Failed to upsert user during Microsoft OAuth. NameId: {NameId}", nameId);
                                identity?.AddClaim(new Claim("UserId", Guid.NewGuid().ToString()));
                            }
                        }
                    };
                });
        }

        services.AddAuthorization();
        return services;
    }
}
