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

        // Rule 13 — In Production, default to Microsoft OAuth as the primary challenge.
        // In Development, GitHub OAuth remains the default for backwards compatibility.
        // SOLID — OCP: the production auth enforcement middleware handles the redirect,
        // so this just sets the default challenge scheme.
        // If neither provider is configured (e.g. local dev without secrets), fall back to
        // the cookie scheme so GUEST mode and other non-OAuth flows still work.
        var ghClientId = configuration[ConfigKeys.GitHub.ClientId];
        var msClientId = configuration[ConfigKeys.Microsoft.ClientId];
        var msClientSecret = configuration[ConfigKeys.Microsoft.ClientSecret];

        string defaultChallengeScheme;
        if (environment.IsDevelopment() && !string.IsNullOrEmpty(ghClientId))
            defaultChallengeScheme = GitHubAuthenticationDefaults.AuthenticationScheme;
        else if (!string.IsNullOrEmpty(msClientId) && !string.IsNullOrEmpty(msClientSecret))
            defaultChallengeScheme = "Microsoft";
        else
            defaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;

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

        // Microsoft OAuth — available in both dev and prod (personal & work Microsoft accounts).
        // Uses the generic OAuth2 handler pointing at the Microsoft identity platform v2 endpoints.
        // Requires Microsoft:ClientId and Microsoft:ClientSecret in configuration / Key Vault.
        // SOLID — OCP: extending auth without modifying GitHub provider configuration.
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
                        // Rule 4.3 — shape-based issuer validation against an allowed tenant-ID list.
                        // /common accepts every Entra tenant + personal MSAs; without this any tenant
                        // could sign in. Equivalent to TokenValidationParameters.ValidateIssuer, but
                        // applied here because the generic OAuth handler does not validate the id_token.
                        // Empty allow-list = accept all (single-tenant deployments leave it unset).
                        var allowedTenants = (configuration[ConfigKeys.Microsoft.AllowedTenants] ?? string.Empty)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (allowedTenants.Length > 0)
                        {
                            var tenantId = ReadTenantId(context.AccessToken)
                                ?? ReadTenantId(context.TokenResponse.Response?.RootElement
                                    .TryGetProperty("id_token", out var idTok) == true ? idTok.GetString() : null);
                            if (tenantId is null || !allowedTenants.Contains(tenantId, StringComparer.OrdinalIgnoreCase))
                            {
                                var log = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                                log.LogWarning("Rejected Microsoft sign-in: tenant {TenantId} not in allow-list.", tenantId ?? "<unknown>");
                                context.Fail("Tenant not allowed.");
                                return;
                            }
                        }

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

        // Rule 3.3 — server-side FallbackPolicy: every endpoint that carries no authorization
        // metadata of its own is authenticated by default. Endpoints that must stay public
        // (/auth/*, /health, /api/feature-flags, the Blazor fallback file, OpenAPI/Scalar in
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
