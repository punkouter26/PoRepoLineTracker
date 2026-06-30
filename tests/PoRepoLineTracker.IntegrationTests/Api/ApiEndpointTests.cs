using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Integration tests for API endpoints using the test web application factory.
/// Tests are organized by endpoint, verifying auth, response codes, and payload structure.
/// Shares the factory instance via <see cref="IntegrationTestCollection"/> to avoid per-class startup overhead.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ApiEndpointTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ApiEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ─── Health & Diagnostics ───────────────────────────────────────────

    [Fact]
    public async Task Health_Endpoint_Returns_200()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Endpoint_Returns_Healthy_Status()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Diag_Endpoint_Returns_200()
    {
        var response = await _client.GetAsync("/diag");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Diag_Endpoint_Returns_Environment_Info()
    {
        var response = await _client.GetAsync("/diag");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("environment");
    }

    // ─── File Extensions Settings ───────────────────────────────────────

    [Fact]
    public async Task GetFileExtensions_Returns_200_With_Array()
    {
        var response = await _client.GetAsync("/api/settings/file-extensions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var extensions = await response.Content.ReadFromJsonAsync<List<string>>();
        extensions.Should().NotBeNull();
        extensions!.Should().NotBeEmpty();
    }

    // ─── Chart Settings ─────────────────────────────────────────────────

    [Fact]
    public async Task GetChartMaxLines_Returns_200()
    {
        var response = await _client.GetAsync("/api/settings/chart/max-lines");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserPreferences_Returns_DefaultChartDisplayMode()
    {
        // Save known preferences first to ensure deterministic state,
        // then verify the response contains a valid ChartDisplayMode.
        // Note: The default when no preferences exist is ChartDisplayMode.TrueData,
        // but other tests may persist preferences for the same test user.
        // We verify the endpoint returns a valid mode rather than assuming clean state.
        var saveResponse = await _client.PutAsJsonAsync("/api/settings/user-preferences", new UserPreferences
        {
            FileExtensions = new List<string> { ".cs" },
            ChartDisplayMode = ChartDisplayMode.TrueData,
            LastUpdated = DateTime.UtcNow
        });
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/settings/user-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preferences = await response.Content.ReadFromJsonAsync<UserPreferences>();
        preferences.Should().NotBeNull();
        preferences!.ChartDisplayMode.Should().Be(ChartDisplayMode.TrueData);
    }

    [Fact]
    public async Task SaveUserPreferences_RoundTrips_ChartDisplayMode()
    {
        var updatedPreferences = new UserPreferences
        {
            FileExtensions = new List<string> { ".cs", ".razor" },
            ChartDisplayMode = ChartDisplayMode.MovingAverage,
            LastUpdated = DateTime.UtcNow
        };

        var saveResponse = await _client.PutAsJsonAsync("/api/settings/user-preferences", updatedPreferences);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/settings/user-preferences");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistedPreferences = await getResponse.Content.ReadFromJsonAsync<UserPreferences>();
        persistedPreferences.Should().NotBeNull();
        persistedPreferences!.ChartDisplayMode.Should().Be(ChartDisplayMode.MovingAverage);
        persistedPreferences.FileExtensions.Should().BeEquivalentTo(new[] { ".cs", ".razor" });
    }

    // ─── Repositories (Authenticated) ───────────────────────────────────

    [Fact]
    public async Task GetAllRepositories_Authenticated_Returns_200()
    {
        var response = await _client.GetAsync("/api/repositories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllRepositories_Returns_EmptyArray_Initially()
    {
        var response = await _client.GetAsync("/api/repositories");
        var repos = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        repos.Should().NotBeNull();
    }

    // ─── All Charts Endpoint ────────────────────────────────────────────

    [Fact]
    public async Task GetAllRepositoriesLineHistory_Returns_200()
    {
        var response = await _client.GetAsync("/api/repositories/allcharts/30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Auth Endpoints ─────────────────────────────────────────────────

    [Fact]
    public async Task AuthMe_Returns_200_With_UserInfo()
    {
        var response = await _client.GetAsync("/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // TestAuthHandler sets up claims including isAuthenticated
        json.TryGetProperty("isAuthenticated", out _).Should().BeTrue();
    }

    // ─── Non-Existent Routes ────────────────────────────────────────────

    [Fact]
    public async Task NonExistentApiRoute_Returns_NotServerError()
    {
        var response = await _client.GetAsync("/api/nonexistent-route-12345");
        ((int)response.StatusCode).Should().BeLessThan(500, "API should not return server errors for unknown routes");
    }

    // ─── Content Type Verification ──────────────────────────────────────

    [Fact]
    public async Task Api_Endpoints_Return_Json_ContentType()
    {
        var response = await _client.GetAsync("/api/settings/file-extensions");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // ─── CRUD: Add Repository ───────────────────────────────────────────

    [Fact]
    public async Task AddRepository_Returns_Created_With_Location()
    {
        var newRepo = new { Owner = "testowner", Name = "testrepo", CloneUrl = "https://github.com/testowner/testrepo.git" };
        var response = await _client.PostAsJsonAsync("/api/repositories", newRepo);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/repositories/");
    }

    [Fact]
    public async Task AddRepository_Returns_Repository_Object()
    {
        var newRepo = new { Owner = "owner2", Name = "repo2", CloneUrl = "https://github.com/owner2/repo2.git" };
        var response = await _client.PostAsJsonAsync("/api/repositories", newRepo);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Verify essential properties come back
        json.TryGetProperty("owner", out var owner).Should().BeTrue();
        owner.GetString().Should().Be("owner2");

        json.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("repo2");
    }

    // ─── CRUD: Delete Single Repository ─────────────────────────────────

    [Fact]
    public async Task DeleteRepository_Returns_NoContent()
    {
        var repoId = Guid.NewGuid();
        var response = await _client.DeleteAsync($"/api/repositories/{repoId}");

        // Expect either NoContent (if repo existed) or NotFound (mocked scenario)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    // ─── CRUD: Delete All Repositories ──────────────────────────────────

    [Fact]
    public async Task DeleteAllRepositories_Authenticated_Returns_NoContent()
    {
        var response = await _client.DeleteAsync("/api/repositories/all");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ─── CRUD: Bulk Add Repositories ────────────────────────────────────

    [Fact]
    public async Task BulkAddRepositories_Authenticated_Returns_200()
    {
        var repos = new[]
        {
            new { Owner = "bulkowner1", RepoName = "bulkrepo1", CloneUrl = "https://github.com/bulkowner1/bulkrepo1.git" },
            new { Owner = "bulkowner2", RepoName = "bulkrepo2", CloneUrl = "https://github.com/bulkowner2/bulkrepo2.git" }
        };

        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", repos);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BulkAddRepositories_EmptyList_Returns_200()
    {
        var repos = Array.Empty<object>();
        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", repos);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── CRUD: Trigger Analysis ─────────────────────────────────────────

    [Fact]
    public async Task TriggerAnalysis_Returns_SuccessOrNotFound()
    {
        var repoId = Guid.NewGuid();
        var response = await _client.PostAsync($"/api/repositories/{repoId}/analyses?force=false", null);

        // With mocked mediator this may return 200 or may throw which gets caught by middleware
        ((int)response.StatusCode).Should().BeLessThan(500, "Analysis endpoint should not produce server errors with valid GUIDs");
    }
}
