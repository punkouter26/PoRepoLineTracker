using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Rule 2.2 — pure API E2E: exercises the running system over HTTP, no UI involved.
/// Targets E2E_BASE_URL (default http://localhost:5000). Each test skips (never fails) when no
/// instance is reachable, so the suite is green on a machine without the app running; CI does
/// not execute tests (Rule 6.4). Ephemeral infra wiring via Testcontainers is a future addition.
/// </summary>
public sealed class HealthAndAuthApiTests
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private static async Task<HttpResponseMessage> GetOrSkipAsync(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            return await client.GetAsync(path);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SkipException($"No app instance reachable at {BaseUrl} ({ex.GetType().Name}).");
        }
    }

    [SkippableFact]
    public async Task Health_Returns_200()
    {
        var response = await GetOrSkipAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task AuthMe_Anonymous_Returns_200_NotAuthenticated()
    {
        var response = await GetOrSkipAsync("/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("isAuthenticated");
    }
}
