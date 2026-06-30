using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PoRepoLineTracker.Api.Extensions;

/// <summary>
/// Rule 4.4 — non-interactive test auth. Maps the request headers
/// <c>X-Fake-User</c> and <c>X-Fake-Roles</c> (comma-separated) to a
/// <see cref="ClaimsPrincipal"/> so E2E/integration suites skip the OAuth loop.
/// Registration is guarded: <see cref="AuthServiceExtensions"/> throws an
/// <see cref="InvalidOperationException"/> if this scheme is wired up in Production.
/// </summary>
public sealed class FakeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Fake";
    public const string UserHeader = "X-Fake-User";
    public const string RolesHeader = "X-Fake-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("UserId", Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.ToString()),
            new("DisplayName", user.ToString()),
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
