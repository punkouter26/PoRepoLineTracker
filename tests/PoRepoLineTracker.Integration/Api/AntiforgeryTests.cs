using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// Proves the antiforgery requirement is actually enforced, not merely configured.
///
/// <see cref="ApiEndpointTests"/> uses a client that performs the token dance, so on its own it
/// would still pass if the middleware were removed. These tests assert the negative: an
/// authenticated caller that omits the token is rejected.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AntiforgeryTests(CustomWebApplicationFactory factory)
{
    // Plain CreateClient(): authenticated by TestAuthHandler, but with no antiforgery handler.
    private HttpClient UntokenedClient => factory.CreateClient();

    private HttpClient TokenedClient => factory.CreateAntiforgeryClient();

    [Fact]
    public async Task TokenEndpoint_Is_Anonymous_Returns_A_Token_And_Is_Not_Cacheable()
    {
        var response = await UntokenedClient.GetAsync("/api/antiforgery/token");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // A proxy replaying one caller's token to another would defeat the whole scheme.
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        payload.Should().NotBeNull();
        payload!.Token.Should().NotBeNullOrWhiteSpace();
        payload.HeaderName.Should().Be("X-CSRF-TOKEN");
    }

    [Fact]
    public async Task EveryWriteVerb_Without_Token_Is_Rejected_And_The_Rejection_Names_The_Header()
    {
        var repo = new[] { new BulkRepositoryDto { Owner = "octocat", RepoName = "hello-world", CloneUrl = "https://github.com/octocat/hello-world.git" } };
        (await UntokenedClient.PostAsJsonAsync("/api/repositories/bulk", repo))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await UntokenedClient.PutAsJsonAsync("/api/settings/user-preferences", new UserPreferences()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var deleteResponse = await UntokenedClient.DeleteAsync("/api/repositories/all");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The failure has to be self-explanatory: a developer hitting this should learn how to
        // fix it from the response, without the validation detail that would help an attacker.
        var body = await deleteResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("antiforgery_validation_failed");
        body.Detail.Should().Contain("X-CSRF-TOKEN");
    }

    [Fact]
    public async Task Get_Requests_Do_Not_Require_A_Token()
    {
        // Safe methods must stay unencumbered — otherwise every page load needs a round trip.
        var response = await UntokenedClient.GetAsync("/api/repositories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_With_Token_Is_Accepted()
    {
        var repo = new[] { new BulkRepositoryDto { Owner = "octocat", RepoName = "tokened", CloneUrl = "https://github.com/octocat/tokened.git" } };

        var response = await TokenedClient.PostAsJsonAsync("/api/repositories/bulk", repo);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_Api_Paths_Are_Not_Gated()
    {
        // The middleware scopes itself to /api so the OAuth callback routes, which are POSTed to
        // cross-site by the provider and carry no token, keep working.
        var response = await UntokenedClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
