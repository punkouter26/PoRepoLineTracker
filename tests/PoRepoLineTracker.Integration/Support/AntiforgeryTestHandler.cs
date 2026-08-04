using System.Net.Http.Json;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// Supplies the antiforgery token on state-changing requests so the integration tier exercises
/// <c>AntiforgeryMiddleware</c> (Rule 4.2) rather than bypassing it.
///
/// <para>Mirrors what the WASM client's <c>AntiforgeryHandler</c> does. Deliberately not a shared
/// implementation: the point of these tests is that a real caller performing the documented
/// token dance is accepted, so the test double proving it should be independent of the
/// production one. If both were the same class, a bug in it would make the tests agree with the
/// client and both be wrong.</para>
///
/// <para>The cookie half rides on <c>WebApplicationFactory</c>'s own cookie container, which
/// <c>CreateDefaultClient</c> installs below this handler; only the header half is added here.</para>
/// </summary>
internal sealed class AntiforgeryTestHandler : DelegatingHandler
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!SafeMethods.Contains(request.Method.Method))
        {
            var token = await EnsureTokenAsync(request.RequestUri, cancellationToken);
            if (token is not null)
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <param name="siblingUri">
    /// The absolute URI of the request being sent, used only for its scheme+authority. HttpClient
    /// applies BaseAddress ABOVE the handler chain, so a request created here starts out relative
    /// and TestHost rejects it ("This operation is not supported for a relative URI"). Deriving
    /// the origin from the outgoing request avoids hardcoding the test host's address.
    /// </param>
    private async Task<string?> EnsureTokenAsync(Uri? siblingUri, CancellationToken cancellationToken)
    {
        if (_token is not null) return _token;

        var tokenUri = siblingUri is { IsAbsoluteUri: true }
            ? new Uri(siblingUri, "/api/antiforgery/token")
            : new Uri("http://localhost/api/antiforgery/token");

        using var request = new HttpRequestMessage(HttpMethod.Get, tokenUri);
        using var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>(cancellationToken);
        _token = payload?.Token;
        return _token;
    }
}
