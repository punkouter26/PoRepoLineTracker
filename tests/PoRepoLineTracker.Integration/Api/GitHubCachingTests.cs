using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// Rule 5.4 — the GitHub repository listing is served through HybridCache. GitHub's REST API is
/// rate-limited per token, so repeated page loads must collapse onto one upstream call.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class GitHubCachingTests
{
    private readonly CustomWebApplicationFactory _factory;

    public GitHubCachingTests(CustomWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// The factory (and therefore the cache) is shared across the collection, so each test must
    /// evict the entry it is about to measure — otherwise a sibling test's warm cache makes the
    /// call-count assertion pass or fail depending on execution order.
    /// </summary>
    private async Task EvictUserRepositoriesAsync()
    {
        var cache = _factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        await cache.RemoveAsync($"github:user-repositories:{TestAuthHandler.TestUserId}");
    }

    [Fact]
    public async Task UserRepositories_RepeatedRequests_HitGitHubOnce()
    {
        await EvictUserRepositoriesAsync();

        var client = _factory.CreateClient();
        var github = _factory.Services.GetRequiredService<IGitHubService>();
        github.ClearReceivedCalls();
        github.GetUserRepositoriesAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IEnumerable<GitHubUserRepositoryDto>>(
                [new GitHubUserRepositoryDto { Name = "cached-repo", Owner = "owner" }]));

        var first = await client.GetAsync("/api/github/user-repositories");
        var second = await client.GetAsync("/api/github/user-repositories");
        var third = await client.GetAsync("/api/github/user-repositories");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.OK);

        await github.Received(1).GetUserRepositoriesAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task UserRepositories_CachedResponse_StillReturnsPayload()
    {
        await EvictUserRepositoriesAsync();

        var client = _factory.CreateClient();
        var github = _factory.Services.GetRequiredService<IGitHubService>();
        github.GetUserRepositoriesAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IEnumerable<GitHubUserRepositoryDto>>(
                [new GitHubUserRepositoryDto { Name = "cached-repo", Owner = "owner" }]));

        await client.GetAsync("/api/github/user-repositories");
        var cached = await client.GetAsync("/api/github/user-repositories");

        var body = await cached.Content.ReadAsStringAsync();
        body.Should().Contain("cached-repo", "a cache hit must return the same payload as a miss");
    }
}
