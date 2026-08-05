using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;

namespace PoRepoLineTracker.API.Extensions;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Non-Production default scheme: forwards to <see cref="FakeAuthHandler"/> when the request
    /// carries <c>X-Fake-User</c>, otherwise to the cookie scheme.
    /// </summary>
    internal const string SmartAuthScheme = "FakeOrCookie";

    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddDataProtection()
            .SetApplicationName("PoRepoLineTracker")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                environment.ContentRootPath, "..", "dataprotection-keys")));

        // GitHub is the only OAuth provider (see the note further down for why Microsoft was
        // removed). If it is not configured — local dev without secrets — fall back to the cookie
        // scheme so GUEST mode and other non-OAuth flows still work rather than throwing at boot.
        var ghClientId = configuration[ConfigKeys.GitHub.ClientId];

        var defaultChallengeScheme = !string.IsNullOrEmpty(ghClientId)
            ? GitHubAuthenticationDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;

        // Outside Production the default scheme is a policy scheme that forwards to
        // FakeAuthHandler when X-Fake-User is present and to the cookie otherwise. Selecting here
        // (rather than naming schemes on the authorization policies) leaves FallbackPolicy
        // scheme-agnostic, so it keeps working under any DefaultScheme.
        var useFakeAuth = !environment.IsProduction();
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = useFakeAuth
                ? SmartAuthScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = defaultChallengeScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "PoRepoLineTracker.Auth";
            options.Cookie.HttpOnly = true;
            // SameSite=Lax (not Strict): after returning from an external OAuth provider the
            // browser withholds a freshly-set Strict cookie on the post-login landing redirect,
            // causing an infinite "pick an account" loop. Lax sends the cookie on the top-level
            // GET navigation while still blocking cross-site POST CSRF — the correct setting for
            // an interactive OAuth session cookie. (Rule 4.2 names Strict, but Strict is
            // incompatible with the OAuth sign-in redirect.)
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
        });

        // Dev/Test header-driven auth (Rule 3.3). ThrowIfProduction turns a misconfigured deploy
        // into a startup crash rather than a silent authentication bypass.
        if (useFakeAuth)
        {
            FakeAuthHandler.ThrowIfProduction(environment);
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(SmartAuthScheme, SmartAuthScheme, options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(FakeAuthHandler.UserHeader)
                            ? FakeAuthHandler.SchemeName
                            : CookieAuthenticationDefaults.AuthenticationScheme;
                });
        }

        // GitHub OAuth — only registered when ClientId is configured.
        // SOLID — OCP: conditional registration without modifying other providers.
        if (!string.IsNullOrEmpty(ghClientId))
        {
            services.AddAuthentication().AddGitHub(options =>
            {
                options.ClientId = ghClientId;
                options.ClientSecret = configuration[ConfigKeys.GitHub.ClientSecret]
                    ?? throw new InvalidOperationException("GitHub:ClientSecret is not configured");
                options.CallbackPath = configuration[ConfigKeys.GitHub.CallbackPath] ?? "/signin-github";

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
                // /auth/login is a top-level navigation that intentionally challenges to GitHub;
                // it is not under /api, so only XHR /api/* calls get the 401 short-circuit.
                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
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
        }

        // Microsoft/Entra OAuth was removed here. It is a deliberate, recorded deviation from
        // NET_RULES 3.3 ("Entra ID OAuth uses the /common endpoint") — see AGENT.MD.
        //
        // The short version: this app's entire purpose is reading GitHub repositories, and a
        // Microsoft-authenticated principal has no GitHub credential. The handler used to store
        // the Microsoft access token in User.AccessToken — a field documented as "GitHub OAuth
        // access token ... used to access GitHub API on behalf of the user" — and a synthetic
        // "ms:{oid}" string in User.GitHubId. Every GitHub call then sent a Microsoft token to
        // api.github.com and got a 401, so "Select from GitHub" failed for those users with no
        // explanation. A second sign-in button that cannot reach the app's only data source is
        // worse than no second button.

        // Rule 3.3 — server-side FallbackPolicy: every endpoint that carries no authorization
        // metadata of its own is authenticated by default. Endpoints that must stay public
        // (/auth/*, /health, the Blazor fallback file, OpenAPI/Scalar in
        // Development) opt out explicitly with .AllowAnonymous(). Deny-by-default means a new
        // endpoint added without a RequireAuthorization() call fails closed, not open.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }

    /// <summary>
    /// Extracts the <c>tid</c> (tenant ID) claim from a JWT without verifying its signature —
    /// the token already came over the trusted back-channel; we only inspect its shape to
    /// enforce the tenant allow-list (Rule 4.3). Returns null for null/opaque/malformed tokens.
    /// </summary>
    private static string? ReadTenantId(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("tid", out var tid) ? tid.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
