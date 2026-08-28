using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Puts a repository with analysed commits behind a fake user and hands back its id.
///
/// <para><b>Why this exists next to the UI tier's seeder rather than reusing it.</b> That one
/// answers "can charts be expected" with a bool and throws the id away, because a browser reaches
/// the repository by clicking a link. A route that takes the id in its URL cannot — it needs the
/// id itself, and reading it back out of <c>/api/repositories</c> would make every assertion
/// depend on a second endpoint's list shape. The seed response already carries it.</para>
///
/// <para><b>Its own owner/name/user, deliberately.</b> Seeding is destructive — the endpoint
/// deletes the existing rows for (owner, name, user) before writing new ones — so sharing
/// <c>e2e/seeded</c> with the UI tier would mean either suite wiping the other's repository out
/// from under a running assertion when both tiers are executed against one instance.</para>
///
/// <para><b>The antiforgery pair is the whole difficulty.</b> The seed route is a POST under
/// /api, so it needs the cookie from <c>GET /api/antiforgery/token</c> AND the same token echoed
/// as <c>X-CSRF-TOKEN</c>. Missing either half answers 400, which reads exactly like "seeding is
/// unavailable" and would quietly turn every test here into a skip.</para>
///
/// <para>Best-effort by design: returns null rather than throwing when the app is not running or
/// the route is absent (a non-Development host does not map it), so callers skip with a message
/// naming which it was.</para>
/// </summary>
internal static class E2EApiSeeder
{
    /// <summary>The account these tests own. Distinct from the UI tier's <c>e2e-ui</c>.</summary>
    internal const string FakeUser = "e2e-api-code-health";

    /// <summary>A second identity, used only to prove the ownership guard refuses it.</summary>
    internal const string OtherFakeUser = "e2e-api-intruder";

    internal const string Owner = "e2e-api";
    internal const string Name = "code-health";

    /// <summary>
    /// Short. Unlike the chart tier, nothing here asserts on the shape of the history — only that
    /// there IS a newest commit for the report to name — so a long series is wasted writes.
    /// </summary>
    private const int Days = 5;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Guid? _repositoryId;
    private static bool _attempted;

    /// <summary>
    /// Seeds once per run and returns the repository id, or null when the app is unreachable or
    /// is not running in Development. Cached because the seed endpoint deletes before it writes:
    /// a second call would hand back a different id and strand any test still holding the first.
    /// </summary>
    internal static async Task<Guid?> EnsureSeededAsync()
    {
        if (_attempted) return _repositoryId;

        await Gate.WaitAsync();
        try
        {
            if (!_attempted)
            {
                _repositoryId = await SeedAsync();
                _attempted = true;
            }
            return _repositoryId;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Guid?> SeedAsync()
    {
        using var handler = new HttpClientHandler
        {
            // E2E_BASE_URL may point at the https profile, which uses the ASP.NET dev certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(E2EApiClient.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("X-Fake-User", FakeUser);

        try
        {
            // The cookie half lands in the container; the token half comes back in the body.
            var tokenResponse = await client.GetAsync("api/antiforgery/token");
            if (!tokenResponse.IsSuccessStatusCode) return null;

            var token = (await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryToken>())?.Token;
            if (string.IsNullOrEmpty(token)) return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/dev/seed/repository")
            {
                Content = JsonContent.Create(new SeedRequest(Owner, Name, Days))
            };
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var seeded = await response.Content.ReadFromJsonAsync<SeedResponse>(CaseInsensitive);
            return seeded?.RepositoryId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // App not running. The caller's Skip message covers it.
            return null;
        }
    }

    /// <summary>
    /// The wire casing is the API's to choose, and no assertion here is about it — reading
    /// case-insensitively keeps these tests from failing over a serializer setting.
    /// </summary>
    internal static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SeedRequest(
        [property: JsonPropertyName("owner")] string Owner,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("days")] int Days);

    private sealed record AntiforgeryToken([property: JsonPropertyName("token")] string Token);

    /// <summary>
    /// Only the id is read back. RepositoryId is serialized as a bare GUID string, so a plain
    /// Guid deserializes it without pulling the strongly-typed id's converter into this tier.
    /// </summary>
    private sealed record SeedResponse(Guid RepositoryId);
}
