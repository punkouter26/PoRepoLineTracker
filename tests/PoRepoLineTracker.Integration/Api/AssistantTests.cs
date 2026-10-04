using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// The optional Claude features with no API key configured — which is how the test host, a fresh
/// clone, and any deployment that has not opted in all run.
///
/// <para>The property worth pinning is that "not configured" is a quiet, complete absence: the
/// status route says so, the narrative is empty rather than an error (the banner calls it on every
/// load), and the question route refuses before anything could be sent anywhere. A regression
/// here is either a 500 on the landing page or a request to an external service nobody enabled.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AssistantTests
{
    private readonly HttpClient _client;

    public AssistantTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateAntiforgeryClient();

    [Fact]
    public async Task WithNoApiKey_TheAssistantIsAbsent_NotBroken()
    {
        var status = await _client.GetFromJsonAsync<AiStatusDto>("/api/insights/assistant");
        status!.Available.Should().BeFalse();

        var narrative = await _client.GetAsync("/api/insights/digest/narrative");
        narrative.StatusCode.Should().Be(HttpStatusCode.OK);
        (await narrative.Content.ReadFromJsonAsync<DigestNarrativeDto>())!.Text.Should().BeNullOrEmpty();

        var ask = await _client.PostAsJsonAsync("/api/repositories/ask", new GridQueryRequest { Query = "which repositories failed?" });
        ask.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_RejectsAnEmptyQuestion_BeforeAnythingElse(string query)
    {
        var response = await _client.PostAsJsonAsync("/api/repositories/ask", new GridQueryRequest { Query = query });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ask_RejectsAnOverlongQuestion()
    {
        var response = await _client.PostAsJsonAsync("/api/repositories/ask", new GridQueryRequest { Query = new string('a', 201) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
