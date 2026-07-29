using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PoRepoLineTracker.API.Auth;

/// <summary>
/// Dev/Test authentication driven entirely by request headers (Rule 3.3), so an automated
/// suite can act as any user without standing up a real OAuth provider:
///
/// <list type="bullet">
///   <item><c>X-Fake-User</c> — the user id. A GUID is used verbatim; any other string is
///     hashed to a stable GUID so <c>X-Fake-User: alice</c> is the same user on every run.</item>
///   <item><c>X-Fake-Roles</c> — comma-separated roles, e.g. <c>admin,auditor</c>.</item>
/// </list>
///
/// A request with no <c>X-Fake-User</c> header is left unauthenticated (NoResult) rather than
/// failed, so the cookie/OAuth schemes still get their turn and anonymous endpoints behave
/// normally.
///
/// Guardrail: <see cref="ThrowIfProduction"/> makes registering this handler in Production a
/// startup crash, not a silent authentication bypass.
/// </summary>
internal sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal const string SchemeName = "Fake";
    internal const string UserHeader = "X-Fake-User";
    internal const string RolesHeader = "X-Fake-Roles";

    public FakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Fails fast when the fake scheme would be wired up in Production. Called from the
    /// composition root before registration, so a misconfigured deploy cannot boot with
    /// header-driven auth enabled.
    /// </summary>
    internal static void ThrowIfProduction(IWebHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{nameof(FakeAuthHandler)} must never be registered in Production — it authenticates " +
                $"any caller that sends an '{UserHeader}' header. Remove the registration or change " +
                "ASPNETCORE_ENVIRONMENT.");
        }
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var rawUser) || string.IsNullOrWhiteSpace(rawUser))
        {
            // No header — defer to the real schemes instead of failing the request.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = rawUser.ToString().Trim();
        var userId = UserId.TryParse(user, out var parsed) ? parsed : new UserId(StableGuid(user));

        var claims = new List<Claim>
        {
            new(ClaimsPrincipalExtensions.UserIdClaim, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, user)
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var rawRoles))
        {
            claims.AddRange(rawRoles
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    /// <summary>
    /// Derives a deterministic GUID from a name so the same header value maps to the same user
    /// across processes and runs — a random GUID would make fixtures unrepeatable.
    /// </summary>
    private static Guid StableGuid(string value)
        => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
