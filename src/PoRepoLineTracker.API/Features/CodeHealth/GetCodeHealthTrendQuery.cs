using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.CodeHealth;

/// <summary>One repository's health month by month, over the trailing two years.</summary>
public record GetCodeHealthTrendQuery(RepositoryId RepositoryId) : IRequest<CodeHealthTrendDto?>;

/// <summary>
/// <para><b>Which commit a month is measured at.</b> The last commit on or before the 1st, UTC —
/// the state the code was actually in that morning. A month with no commit before it is skipped
/// rather than guessed at: inventing a point for a repository that did not exist yet would draw a
/// line through history that never happened. Consecutive months can resolve to the SAME commit when
/// nothing was pushed in between, and that is correct — the code did not change, so neither should
/// the score.</para>
///
/// <para><b>Why this is affordable at all.</b> Every figure is a pure function of the commit's tree,
/// so it is memoised per SHA and computed at most once ever. A first run pays for up to twenty-four
/// snapshots; every run after that reads storage. Months resolving to a repeated commit are
/// deduplicated before any work happens, so a quiet year costs one measurement, not twelve.</para>
///
/// <para><b>The memo is loaded in ONE query, before the loop.</b> Reading it per commit inside the
/// loop is precisely the pattern that got <c>CommitExistsAsync</c> deleted — a point read per SHA
/// turning a long history into thousands of round-trips to answer what a single partition scan
/// already returns.</para>
/// </summary>
public sealed class GetCodeHealthTrendQueryHandler(
    IRepositoryDataService repositoryDataService,
    IGitHubService gitHubService,
    IUserPreferencesService userPreferencesService,
    ICodeHealthSnapshotStore snapshotStore,
    ILogger<GetCodeHealthTrendQueryHandler> logger)
    : IRequestHandler<GetCodeHealthTrendQuery, CodeHealthTrendDto?>
{
    /// <summary>Two years: long enough to show a slow drift, bounded enough that a first run ends.</summary>
    private const int MonthsBack = 24;

    public async Task<CodeHealthTrendDto?> Handle(GetCodeHealthTrendQuery request, CancellationToken cancellationToken)
    {
        var repository = await repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
        if (repository is null) return null;

        var commits = (await repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId))
            .OrderBy(c => c.CommitDate)
            .ToList();

        if (commits.Count == 0) return null;

        // A repository whose whole history sits inside one month has commits but crosses no
        // boundary, so there is nothing to plot — and that is NOT the same as having no commits.
        // Returning null here reported "no analysed commits yet" for a repository actively being
        // committed to, which is simply false. An empty series says "not enough history" instead.
        var months = ResolveMonthlyCommits(commits);
        if (months.Count == 0)
        {
            return new CodeHealthTrendDto
            {
                RepositoryId = repository.Id,
                Owner = repository.Owner,
                Name = repository.Name,
                Points = []
            };
        }

        // One partition scan for the whole memo, before anything is measured.
        var memo = await snapshotStore.GetByRepositoryAsync(request.RepositoryId);

        var repositoryPath = gitHubService.ResolveRepositoryPath(repository.LocalPath);
        var hasClone = !string.IsNullOrWhiteSpace(repositoryPath) && Directory.Exists(repositoryPath);

        var extensions = repository.UserId != UserId.Empty
            ? await userPreferencesService.GetFileExtensionsAsync(repository.UserId)
            : UserPreferences.DefaultFileExtensions;

        var points = new List<CodeHealthTrendPointDto>(months.Count);
        var computed = 0;

        foreach (var (month, commit) in months)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (memo.TryGetValue(commit.CommitSha, out var cachedSnapshot))
            {
                points.Add(ToPoint(month, cachedSnapshot));
                continue;
            }

            // Nothing to read the tree from. The month is reported as a gap rather than dropped:
            // "we could not measure this" and "the repository was healthy" must not look alike.
            if (!hasClone) continue;

            try
            {
                var report = await Task.Run(
                    () => CodeHealthReportFactory.Build(
                        gitHubService.EnumerateSourceFiles(repositoryPath, commit.CommitSha, extensions)),
                    cancellationToken);

                report.CommitDate = commit.CommitDate;

                var entity = CodeHealthSnapshotSerializer.ToEntity(request.RepositoryId, commit.CommitSha, report);
                await snapshotStore.SaveAsync(entity);
                computed++;

                points.Add(ToPoint(month, entity));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable commit is a gap in the line, not a failed chart. A shallow clone
                // in particular simply does not contain the older trees.
                logger.LogWarning(ex, "Could not measure {RepositoryId} at {Sha} for {Month:yyyy-MM}",
                    request.RepositoryId, commit.CommitSha, month);
            }
        }

        // Boundaries existed but none could be measured — a missing clone, or a shallow one that
        // does not contain the older trees. Still an empty series rather than a 404: the repository
        // is fine, the measurement is what is missing.
        if (points.Count == 0)
        {
            return new CodeHealthTrendDto
            {
                RepositoryId = repository.Id,
                Owner = repository.Owner,
                Name = repository.Name,
                Points = []
            };
        }

        logger.LogInformation(
            "Code health trend for {Owner}/{Name}: {Points} months, {Computed} newly measured, {Cached} from the memo",
            repository.Owner, repository.Name, points.Count, computed, points.Count - computed);

        return new CodeHealthTrendDto
        {
            RepositoryId = repository.Id,
            Owner = repository.Owner,
            Name = repository.Name,
            Points = points
        };
    }

    /// <summary>
    /// One commit per month for the trailing window: the last one on or before each 1st.
    ///
    /// <para>Walks the months against a commit list already sorted by date, rather than scanning the
    /// whole history per month — the same reason the analysis handler pre-loads its SHA set instead
    /// of asking storage per commit.</para>
    /// </summary>
    private static List<(DateTime Month, CommitLineCount Commit)> ResolveMonthlyCommits(
        IReadOnlyList<CommitLineCount> ascendingByDate)
    {
        var result = new List<(DateTime, CommitLineCount)>();
        var today = DateTime.UtcNow;
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var index = 0;
        CommitLineCount? latestSoFar = null;
        string? lastSha = null;

        for (var back = MonthsBack - 1; back >= 0; back--)
        {
            var month = firstOfThisMonth.AddMonths(-back);

            // Advance through the commits up to this boundary, keeping the last one seen.
            while (index < ascendingByDate.Count && ascendingByDate[index].CommitDate <= month)
            {
                latestSoFar = ascendingByDate[index];
                index++;
            }

            // No commit yet: the repository did not exist at this boundary, so there is nothing to
            // report and a zero would read as a verdict.
            if (latestSoFar is null) continue;

            // A quiet month resolves to the same commit as the previous one. Recording it again
            // would measure identical bytes twice for one flat segment of the line.
            if (lastSha == latestSoFar.CommitSha)
            {
                result.Add((month, latestSoFar));
                continue;
            }

            lastSha = latestSoFar.CommitSha;
            result.Add((month, latestSoFar));
        }

        return result;
    }

    private static CodeHealthTrendPointDto ToPoint(DateTime month, CodeHealthSnapshotEntity snapshot) => new()
    {
        Month = month,
        Score = snapshot.Score,
        Grade = snapshot.Grade,

        // -1 is the stored sentinel for "this commit held no C#"; it must not surface as an index.
        MaintainabilityIndex = snapshot.MaintainabilityIndex >= 0 ? snapshot.MaintainabilityIndex : null,

        CyclomaticComplexity = snapshot.CyclomaticComplexity,
        LinesOfSourceCode = snapshot.LinesOfSourceCode,
        CommitSha = snapshot.CommitSha.Length > 7 ? snapshot.CommitSha[..7] : snapshot.CommitSha,
        CommitDate = snapshot.CommitDate
    };
}
