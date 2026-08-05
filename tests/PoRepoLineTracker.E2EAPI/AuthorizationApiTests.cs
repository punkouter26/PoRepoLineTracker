using System.Net;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// Rule 3.3 — deny-by-default, verified against a real running instance rather than a test host.
/// The integration tier proves the policy is configured; this tier proves the deployed process
/// actually refuses an anonymous caller on every protected route.
/// </summary>
public sealed class AuthorizationApiTests
{
    /// <summary>401 (API call) or a 302 to the sign-in provider both count as "refused".</summary>
    private static void ShouldBeRefused(HttpResponseMessage response) =>
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.Found);

    [SkippableTheory]
    [InlineData("/api/repositories")]
    [InlineData("/api/repositories/allcharts/30")]
    [InlineData("/api/settings/user-preferences")]
    [InlineData("/api/github/user-repositories")]
    [InlineData("/api/diagnostics")]
    public async Task ProtectedGet_Anonymous_IsRefused(string route)
    {
        var response = await E2EApiClient.GetAsync(route);

        ShouldBeRefused(response);
    }

    [SkippableFact]
    public async Task ProtectedPost_Anonymous_IsRefused()
    {
        var response = await E2EApiClient.PostAsync("/api/repositories/bulk");

        ShouldBeRefused(response);
    }

    [SkippableFact]
    public async Task ProtectedDelete_Anonymous_IsRefused()
    {
        var response = await E2EApiClient.DeleteAsync("/api/repositories/all");

        ShouldBeRefused(response);
    }

    [SkippableFact]
    public async Task ProtectedPut_Anonymous_IsRefused()
    {
        var response = await E2EApiClient.PutAsync("/api/settings/user-preferences");

        ShouldBeRefused(response);
    }

    [SkippableFact]
    public async Task PerRepositoryRoute_Anonymous_IsRefusedBeforeLookup()
    {
        // A refusal, not a 404: authorization must run before the endpoint reveals whether a
        // given repository id exists.
        var response = await E2EApiClient.GetAsync($"/api/repositories/{Guid.NewGuid()}/top-files");

        ShouldBeRefused(response);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task UploadEndpoint_Anonymous_IsRefused()
    {
        var response = await E2EApiClient.PostAsync("/api/repositories/upload-zip");

        ShouldBeRefused(response);
    }

    [SkippableFact]
    public async Task RefusedApiCall_DoesNotRedirectCrossOrigin()
    {
        // /api/* must answer XHR with 401 instead of a cross-origin redirect to the OAuth
        // provider, which the browser would surface as an opaque CORS error.
        var response = await E2EApiClient.GetJsonAsync("/api/repositories");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task RefusedCall_LeaksNoRepositoryData()
    {
        var response = await E2EApiClient.GetAsync("/api/repositories");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("cloneUrl");
    }
}
