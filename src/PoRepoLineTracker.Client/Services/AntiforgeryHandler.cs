using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Shared.Serialization;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Attaches the antiforgery request token to every state-changing call (Rule 4.2).
///
/// <para>The cookie half of the pair is <c>HttpOnly</c>, so the client cannot read it — that is
/// what makes the scheme work, and it is why the token has to be fetched from
/// <c>/api/antiforgery/token</c> rather than pulled out of <c>document.cookie</c>.</para>
///
/// <para>Doing this in a handler rather than at each call site means a new POST/PUT/DELETE
/// anywhere in the app is protected by default; there is no per-call step to forget.</para>
///
/// <para><b>Refresh.</b> The token is cached for the session but is not permanent — it is bound
/// to the authentication cookie, so signing in or out invalidates it. Rather than track that,
/// the handler retries once on the 400 the server returns for a stale token, which also covers
/// an app-restart mid-session. The retry is capped at one attempt so a genuinely rejected
/// request cannot loop.</para>
/// </summary>
internal sealed class AntiforgeryHandler(IServiceProvider services) : DelegatingHandler
{
    private const string HeaderName = "X-CSRF-TOKEN";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>Guards the fetch so a burst of parallel writes causes one token request, not N.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (SafeMethods.Contains(request.Method.Method))
            return await base.SendAsync(request, cancellationToken);

        var token = await GetTokenAsync(forceRefresh: false, cancellationToken);
        ApplyToken(request, token);

        var response = await base.SendAsync(request, cancellationToken);

        // A stale token reads as 400 from AntiforgeryMiddleware. Re-fetch once and replay.
        // The request has to be cloned: an HttpRequestMessage cannot be sent twice.
        if (response.StatusCode == HttpStatusCode.BadRequest && token is not null)
        {
            var refreshed = await GetTokenAsync(forceRefresh: true, cancellationToken);
            if (refreshed is not null && refreshed != token)
            {
                response.Dispose();
                var retry = await CloneAsync(request, cancellationToken);
                ApplyToken(retry, refreshed);
                return await base.SendAsync(retry, cancellationToken);
            }
        }

        return response;
    }

    private static void ApplyToken(HttpRequestMessage request, string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, token);
    }

    private async Task<string?> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null) return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: a request that queued behind the fetch should reuse its
            // result rather than immediately fetch a second token.
            if (!forceRefresh && _token is not null) return _token;

            // A bare HttpClient, not the one this handler is attached to — routing the fetch back
            // through the pipeline would re-enter SendAsync and deadlock on _gate.
            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRaw");
            var response = await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
                "/api/antiforgery/token", AppJsonOptions.Default, cancellationToken);

            _token = response?.Token;
            return _token;
        }
        catch (HttpRequestException)
        {
            // Offline or the endpoint is unreachable. Returning null lets the original request
            // proceed and fail with the server's own status, which is more informative than an
            // exception thrown from a handler.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        if (request.Content is not null)
        {
            // Buffer before re-reading: the original content stream is already consumed.
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in request.Options)
            clone.Options.TryAdd(option.Key, option.Value);

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }
}
