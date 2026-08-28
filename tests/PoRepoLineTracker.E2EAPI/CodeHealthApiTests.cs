using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace PoRepoLineTracker.E2EAPI;

/// <summary>
/// <c>GET /api/code-health/{repositoryId}</c>, against a real running instance.
///
/// <para><b>What this tier adds over the ones either side of it.</b> The unit tier proves
/// <c>CodeHealthScoring</c> arithmetic against hand-built metrics, and the UI tier proves the card
/// renders. Neither exercises the route: the ownership guard, the 404-vs-403 distinction, and the
/// no-data contract are all decided in the endpoint and the query handler, and until now nothing
/// asked the deployed process for a report at all.</para>
///
/// <para><b>The scored path is not reachable from here, and that is a property of the feature.</b>
/// The report is computed on demand from the working clone — the source text is the one thing
/// analysis does not store — so a repository with commit rows but no git objects on disk has
/// nothing to measure. Seeding writes exactly that. So these pin the boundary the seeded state
/// actually reaches, and the assertion that matters most is the negative one: an unmeasurable
/// repository must SAY so, not present a zero as a grade. Grading the arithmetic itself stays with
/// <c>CodeHealthScoringTests</c>, which can hand it real files.</para>
///
/// <para>Anonymous refusal is already covered for this route by <c>AuthorizationApiTests</c> and
/// is not repeated here.</para>
/// </summary>
public sealed class CodeHealthApiTests
{
    private static async Task<Guid> SeededRepositoryIdAsync()
    {
        var id = await E2EApiSeeder.EnsureSeededAsync();
        Skip.If(id is null,
            $"Could not seed a repository at {E2EApiClient.BaseUrl} — is the app running in Development?");
        return id!.Value;
    }

    private static async Task<CodeHealthResponse> GetReportAsync(Guid repositoryId)
    {
        var response = await E2EApiClient.GetJsonAsync($"/api/code-health/{repositoryId}", E2EApiSeeder.FakeUser);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the seeded repository has analysed commits, so there is a commit to report on");

        var report = await response.Content.ReadFromJsonAsync<CodeHealthResponse>(E2EApiSeeder.CaseInsensitive);
        report.Should().NotBeNull("a 200 must carry a report body");
        return report!;
    }

    [SkippableFact]
    public async Task Report_NamesTheRepositoryAndTheCommitItMeasured()
    {
        var repositoryId = await SeededRepositoryIdAsync();

        var report = await GetReportAsync(repositoryId);

        report.RepositoryId.Should().Be(repositoryId);
        report.Owner.Should().Be(E2EApiSeeder.Owner);
        report.Name.Should().Be(E2EApiSeeder.Name);

        // The report is a statement about one commit — the newest — and has to say which, or the
        // number cannot be reproduced or compared against a later run.
        report.CommitSha.Should().NotBeNullOrWhiteSpace();
        report.CommitSha.Length.Should().BeLessThanOrEqualTo(7, "the endpoint abbreviates the sha");
        report.CommitDate.Should().NotBe(default);
    }

    /// <summary>
    /// The one that would catch a real regression. <c>CodeHealthScoring.Build</c> returns early
    /// for an empty file set precisely so that "nothing to measure" cannot be mistaken for
    /// "measured, and terrible" — a composite of no factors is 0, and 0 grades as F. If someone
    /// moves the grade assignment above that early return, every unanalysable repository starts
    /// reporting a confident F and nothing else in the suite notices.
    /// </summary>
    [SkippableFact]
    public async Task WithNoSourceTreeOnDisk_SaysThereIsNothingToMeasure_RatherThanGradingZeroAsAnF()
    {
        var repositoryId = await SeededRepositoryIdAsync();

        var report = await GetReportAsync(repositoryId);

        report.HasData.Should().BeFalse(
            "the seeded repository has commit rows but no git objects, so no file could be read");

        report.FilesAnalyzed.Should().Be(0);
        report.CodeLines.Should().Be(0);

        report.Grade.Should().BeNullOrEmpty(
            "an unmeasurable repository must carry NO grade — an F here is indistinguishable from a real one");
        report.Score.Should().Be(0);

        // Nothing to break down, and above all nothing to act on: a hotspot list would be naming
        // files that were never opened.
        report.Factors.Should().BeEmpty();
        report.Hotspots.Should().BeEmpty();
    }

    /// <summary>
    /// The ownership guard. This route takes a repository id straight from the URL, and a report
    /// carries file paths and hotspots out of the repository — so a miss here leaks the shape of
    /// someone else's private code, and it leaks it to any signed-in caller who can guess an id.
    /// </summary>
    [SkippableFact]
    public async Task AnotherUsersRepository_IsRefused()
    {
        var repositoryId = await SeededRepositoryIdAsync();

        var response = await E2EApiClient.GetJsonAsync(
            $"/api/code-health/{repositoryId}", E2EApiSeeder.OtherFakeUser);

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
            "a caller who does not own the repository must not receive its report");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task UnknownRepository_Is404_NotAnEmptyReport()
    {
        var response = await E2EApiClient.GetJsonAsync(
            $"/api/code-health/{Guid.NewGuid()}", E2EApiSeeder.FakeUser);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a repository that does not exist has no report, and a body of zeroes would read as one");
    }

    /// <summary>
    /// A repository id that cannot be parsed is a client mistake, not a server fault. Asserted as
    /// "not 5xx" rather than one exact code because whether it is refused by route matching or by
    /// the parameter binder is a framework detail, and both answers are correct.
    /// </summary>
    [SkippableFact]
    public async Task MalformedRepositoryId_IsRejected_NotAServerError()
    {
        var response = await E2EApiClient.GetJsonAsync(
            "/api/code-health/not-a-guid", E2EApiSeeder.FakeUser);

        ((int)response.StatusCode).Should().BeLessThan(500,
            "a malformed id in the URL must not reach anything that can throw");
    }

    /// <summary>
    /// Mirrors only the fields asserted above. A test-local shape rather than the real
    /// <c>CodeHealthDto</c> on purpose: deserializing into the shipped type would make these pass
    /// on a contract that changed shape underneath them, which is the opposite of what an E2E
    /// assertion is for.
    /// </summary>
    private sealed record CodeHealthResponse(
        Guid RepositoryId,
        string Owner,
        string Name,
        string CommitSha,
        DateTime CommitDate,
        bool HasData,
        int Score,
        string? Grade,
        int FilesAnalyzed,
        int CodeLines,
        List<object> Factors,
        List<object> Hotspots);
}
