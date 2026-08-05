using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoRepoLineTracker.E2EUI;

/// <summary>
/// Puts synthetic repository history behind the suite's fake user so the chart assertions have
/// something to assert on.
///
/// <para>Twelve tests in <see cref="ChartAndShellUiTests"/> used to open with
/// <c>Skip.If(no chart rendered)</c> and skip on every run, because charts only render for a
/// signed-in user with analysed repositories and the fake user had none. The suite reported
/// "45 passed, 12 skipped" while covering no chart at all.</para>
///
/// <para><b>The antiforgery dance is the whole difficulty.</b> The seed route is a POST under
/// /api, so AntiforgeryMiddleware requires the cookie/header pair: GET /api/antiforgery/token,
/// keep the Set-Cookie, echo the token back as X-CSRF-TOKEN. Missing either half answers 400,
/// which reads exactly like "seeding is not available" and would send the tests straight back to
/// skipping. Hence a CookieContainer here and an explicit token fetch.</para>
///
/// <para>Seeding is best-effort by design: it returns false rather than throwing when the app is
/// not running or the route is absent (a non-Development host does not map it). Callers then skip
/// with a message that says which of those it was.</para>
/// </summary>
internal static class E2ESeeder
{
    /// <summary>
    /// Names the seeded repository. Fixed so the endpoint's own idempotency can recognise and
    /// replace it, rather than stacking a new repository per run.
    /// </summary>
    internal const string Owner = "e2e";
    internal const string Name = "seeded";

    /// <summary>
    /// Long enough that the 30/90 ranges both have data and the category axis has to thin its
    /// ticks, short enough to write quickly.
    /// </summary>
    internal const int DefaultDays = 120;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool? _seeded;

    /// <summary>
    /// Seeds once per test run and reports whether charts can be expected. Cached because the
    /// collection fixture is shared: without it, every chart test would re-seed, and the deletes
    /// the endpoint does first would race a page that is mid-render against the old rows.
    /// </summary>
    internal static async Task<bool> EnsureSeededAsync()
    {
        if (_seeded is { } cached) return cached;

        await Gate.WaitAsync();
        try
        {
            _seeded ??= await SeedAsync();
            return _seeded.Value;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> SeedAsync()
    {
        // The local instance uses the ASP.NET dev certificate, which this handler does not trust
        // by default — same allowance the browser contexts make.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(E2EUiFixture.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("X-Fake-User", E2EUiFixture.FakeUser);

        try
        {
            // The cookie half lands in the container; the token half comes back in the body.
            var tokenResponse = await client.GetAsync("api/antiforgery/token");
            if (!tokenResponse.IsSuccessStatusCode) return false;

            var token = (await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryToken>())?.Token;
            if (string.IsNullOrEmpty(token)) return false;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/dev/seed/repository")
            {
                Content = JsonContent.Create(new SeedRequest(Owner, Name, DefaultDays))
            };
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);

            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // App not running. The caller's Skip message covers it.
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private sealed record SeedRequest(
        [property: JsonPropertyName("owner")] string Owner,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("days")] int Days);

    private sealed record AntiforgeryToken([property: JsonPropertyName("token")] string Token);
}
