using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Integration;

/// <summary>
/// The recap and digest routes end to end, through the real endpoint pipeline and real Azure Table
/// Storage.
///
/// <para>The unit tier already covers the arithmetic. What only this tier can prove is the part
/// that spans a storage round-trip: <c>POST /api/insights/digest/seen</c> writes the visit through
/// <c>SavePreferencesAsync</c>, which upserts with <c>TableUpdateMode.Replace</c>. Get that wrong —
/// build the preferences object from anything less than the stored row — and recording a visit
/// silently resets the user's counted-extensions list. Nothing in the unit tier would notice, and
/// the symptom on a real account is every repository quietly re-analysing against the defaults.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RecapAndDigestTests
{
    private readonly HttpClient _client;

    public RecapAndDigestTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateAntiforgeryClient();

    // ─── Recap ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Recap_WithNoYear_Returns_200_ForTheCurrentYear()
    {
        var response = await _client.GetAsync("/api/recap");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var recap = await response.Content.ReadFromJsonAsync<YearInCodeDto>();
        recap.Should().NotBeNull();
        recap!.Year.Should().Be(DateTime.UtcNow.Year);
        recap.IsPartialYear.Should().BeTrue();
    }

    [Fact]
    public async Task Recap_WithAnExplicitYear_Returns_ThatYear()
    {
        var response = await _client.GetAsync("/api/recap/2020");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var recap = await response.Content.ReadFromJsonAsync<YearInCodeDto>();
        recap!.Year.Should().Be(2020);
        recap.IsPartialYear.Should().BeFalse();
        // A year with nothing in it still returns full-length buckets, so the page never indexes
        // past the end of a short list.
        recap.CommitsByHour.Should().HaveCount(24);
        recap.CommitsByMonth.Should().HaveCount(12);
    }

    [Theory]
    [InlineData(1969)]
    public async Task Recap_WithAnOutOfRangeYear_Is_Rejected(int year)
    {
        var response = await _client.GetAsync($"/api/recap/{year}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recap_WithANonNumericYear_Does_Not_Match_The_Route()
    {
        // The {year:int} constraint must reject this rather than letting it reach the handler.
        var response = await _client.GetAsync("/api/recap/last-year");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Digest ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Digest_Returns_200_With_AWindowItDeclares()
    {
        var response = await _client.GetAsync("/api/insights/digest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var digest = await response.Content.ReadFromJsonAsync<WeeklyDigestDto>();
        digest.Should().NotBeNull();
        digest!.SinceUtc.Should().BeBefore(digest.UntilUtc,
            "the banner labels itself from this window, so it has to be a real one");
    }

    /// <summary>
    /// The regression this whole file exists for. Recording a visit must preserve everything else
    /// on the preferences row.
    /// </summary>
    [Fact]
    public async Task MarkingTheDigestSeen_Preserves_The_Users_CountedExtensions()
    {
        var chosen = new List<string> { ".cs", ".fs", ".razor" };
        var save = await _client.PutAsJsonAsync("/api/settings/user-preferences",
            new UserPreferences { FileExtensions = chosen, LastUpdated = DateTime.UtcNow });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        var seen = await _client.PostAsync("/api/insights/digest/seen", content: null);
        seen.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetFromJsonAsync<UserPreferences>("/api/settings/user-preferences");
        after.Should().NotBeNull();
        after!.FileExtensions.Should().BeEquivalentTo(chosen,
            "recording a visit upserts the whole row, so it must carry the stored extensions with it");
        after.LastSeenUtc.Should().NotBeNull("the visit is what the write was for");
    }

    /// <summary>
    /// The read must not consume the window. If fetching the digest also stamped the visit, a page
    /// opened and closed without the user looking would leave nothing for the next real visit.
    /// </summary>
    [Fact]
    public async Task Reading_The_Digest_Does_Not_Record_A_Visit()
    {
        // Start from a known state: no recorded visit.
        await _client.PutAsJsonAsync("/api/settings/user-preferences",
            new UserPreferences { FileExtensions = [".cs"], LastUpdated = DateTime.UtcNow });

        await _client.GetAsync("/api/insights/digest");

        var after = await _client.GetFromJsonAsync<UserPreferences>("/api/settings/user-preferences");
        after!.LastSeenUtc.Should().BeNull("only the explicit 'seen' call may record a visit");
    }
}
