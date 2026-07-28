using System.Net.Http.Headers;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Shared HTTP access for the pure-API E2E tier (Rule 2.2). Targets a *running* instance at
/// E2E_BASE_URL (default http://localhost:5000).
///
/// Every request routes through here so that "the app is not running" is reported as a skip
/// rather than a failure: this tier is executed locally and against the Test environment, and CI
/// does not run tests (Rule 6.4), so an unreachable instance is an expected state, not a defect.
/// A real HTTP status — including a 401 — is always a result, never a skip.
/// </summary>
internal static class E2EApiClient
{
    internal static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        // Follow-redirect off: a 302 to the OAuth provider is itself the assertion in several tests.
        AllowAutoRedirect = false
    })
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(15)
    };

    internal static Task<HttpResponseMessage> GetAsync(string path)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path));

    internal static Task<HttpResponseMessage> PostAsync(string path, HttpContent? content = null)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = content });

    internal static Task<HttpResponseMessage> DeleteAsync(string path)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, path));

    internal static Task<HttpResponseMessage> PutAsync(string path, HttpContent? content = null)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Put, path) { Content = content });

    internal static Task<HttpResponseMessage> GetJsonAsync(string path)
        => SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        });

    private static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory)
    {
        using var client = CreateClient();
        try
        {
            return await client.SendAsync(requestFactory());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SkipException($"No app instance reachable at {BaseUrl} ({ex.GetType().Name}).");
        }
    }
}
