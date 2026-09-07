using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.Insights;

/// <summary>
/// Builds the "what happened while you were away" figure set.
/// </summary>
/// <param name="UserId">Whose portfolio to report on.</param>
/// <param name="LastSeenUtc">
/// The user's last recorded visit, or null if none. The handler — not the caller — decides whether
/// that timestamp is a usable window start; see <see cref="GetWeeklyDigestQueryHandler"/>.
/// </param>
public record GetWeeklyDigestQuery(UserId UserId, DateTime? LastSeenUtc) : IRequest<WeeklyDigestDto>;

/// <summary>
/// <para><b>Why this lives beside the portfolio query rather than in its own slice.</b> It answers
/// the same question over a different window and reads exactly the same data, so a separate slice
/// would mean a second copy of the fetch-every-repository's-commits fan-out. It is a distinct
/// handler rather than a parameter on the portfolio query because the two have almost no overlap
/// in output: the dashboard wants a year of heatmap cells and a language mix, the banner wants six
/// numbers and a comparison against the preceding window.</para>
/// </summary>
public sealed class GetWeeklyDigestQueryHandler(
    IRepositoryDataService repositoryDataService,
    ILogger<GetWeeklyDigestQueryHandler> logger)
    : IRequestHandler<GetWeeklyDigestQuery, WeeklyDigestDto>
{
    /// <summary>Window used when there is no usable last-visit timestamp.</summary>
    private const int FallbackWindowDays = 7;

    /// <summary>
    /// Below this, a last visit is ignored and the fallback window is used instead. Someone who
    /// reloaded twenty minutes ago has no news, and a banner announcing "0 commits since you were
    /// last here" is worse than no banner — it makes the feature look broken on the one path
    /// (an engaged user) it most needs to look good on.
    /// </summary>
    private const int MinimumAwayHours = 12;

    /// <summary>
    /// Above this, the last visit is ignored too. A year-old timestamp would report the whole year
    /// under a "since you were last here" label — true, but a banner is the wrong surface for it,
    /// and the dashboard behind it already reports the year.
    /// </summary>
    private const int MaximumAwayDays = 90;

    /// <summary>Repositories listed in the banner's "busiest" row.</summary>
    private const int TopRepoCount = 3;

    /// <summary>Span the streak figure searches, matching the Insights dashboard's own window.</summary>
    private const int StreakWindowDays = 365;

    public async Task<WeeklyDigestDto> Handle(GetWeeklyDigestQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var (since, isSinceLastVisit) = ResolveWindow(request.LastSeenUtc, now);

        var digest = new WeeklyDigestDto
        {
            SinceUtc = since,
            UntilUtc = now,
            IsSinceLastVisit = isSinceLastVisit,
            LastVisitUtc = request.LastSeenUtc
        };

        var repositories = (await repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();
        if (repositories.Count == 0) return digest;

        // Same parallel fan-out as the portfolio query, lifted to the data service so a fourth
        // handler cannot accidentally re-serialise it. Commits arrive ordered by date ascending.
        var commitsPerRepo = await repositoryDataService.GetAllRepositoriesWithCommitsAsync(request.UserId);

        // The window immediately before this one, of equal length, so "up from last time" compares
        // like with like however long the user was away.
        var windowLength = now - since;
        var previousSince = since - windowLength;

        var today = now.Date;
        var streakCutoff = today.AddDays(-(StreakWindowDays - 1));
        var activeDates = new HashSet<DateTime>();
        var windowDays = new HashSet<DateTime>();
        var touched = new List<DigestRepoDto>();

        long linesAdded = 0, linesRemoved = 0, previousLinesAdded = 0;
        int commits = 0, previousCommits = 0;

        foreach (var (repo, repoCommits) in commitsPerRepo)
        {
            if (repoCommits.Count == 0) continue;

            var repoCommitCount = 0;

            foreach (var commit in repoCommits)
            {
                if (commit.CommitDate.Date >= streakCutoff && commit.CommitDate <= now)
                    activeDates.Add(commit.CommitDate.Date);

                if (commit.CommitDate >= since && commit.CommitDate <= now)
                {
                    repoCommitCount++;
                    commits++;
                    linesAdded += commit.LinesAdded;
                    linesRemoved += commit.LinesRemoved;
                    windowDays.Add(commit.CommitDate.Date);
                }
                else if (commit.CommitDate >= previousSince && commit.CommitDate < since)
                {
                    previousCommits++;
                    previousLinesAdded += commit.LinesAdded;
                }
            }

            // Net growth is measured for every repository, not only the ones committed to in the
            // window — a repository can be re-analysed with different counted extensions and change
            // size without a new commit landing.
            var growth = RepositoryTotals.LatestTotalLines(repoCommits) - RepositoryTotals.TotalLinesAsOf(repoCommits, since);
            digest.NetGrowth += growth;

            if (repoCommitCount > 0)
            {
                touched.Add(new DigestRepoDto
                {
                    RepositoryId = repo.Id,
                    Owner = repo.Owner,
                    Name = repo.Name,
                    Commits = repoCommitCount,
                    NetGrowth = growth
                });
            }
        }

        digest.Commits = commits;
        digest.LinesAdded = (int)Math.Min(linesAdded, int.MaxValue);
        digest.LinesRemoved = (int)Math.Min(linesRemoved, int.MaxValue);
        digest.PreviousCommits = previousCommits;
        digest.PreviousLinesAdded = (int)Math.Min(previousLinesAdded, int.MaxValue);
        digest.ReposTouched = touched.Count;
        digest.ActiveDays = windowDays.Count;
        digest.CurrentStreakDays = CommitStreaks.Current(activeDates, today);
        digest.TopRepos = touched
            .OrderByDescending(r => r.Commits)
            .ThenByDescending(r => r.NetGrowth)
            .Take(TopRepoCount)
            .ToList();

        // A window with no commits AND no size change is genuinely nothing to report. Net growth
        // alone counts as activity, because a re-analysis that moved the portfolio total is
        // exactly the kind of change a user coming back would want flagged.
        digest.HasActivity = digest.Commits > 0 || digest.NetGrowth != 0;

        logger.LogInformation(
            "Digest for user {UserId}: {Commits} commits over {Hours:F1}h (since last visit: {SinceLastVisit})",
            request.UserId, digest.Commits, (now - since).TotalHours, isSinceLastVisit);

        return digest;
    }

    /// <summary>
    /// Picks the window start and reports whether it is the user's actual last visit.
    ///
    /// <para>A last visit is only used when it sits inside a sensible band — see
    /// <see cref="MinimumAwayHours"/> and <see cref="MaximumAwayDays"/>. Anything else, including a
    /// timestamp somehow in the future (a clock skew, or a row written by a machine ahead of this
    /// one), falls back to the fixed trailing window.</para>
    /// </summary>
    private static (DateTime Since, bool IsSinceLastVisit) ResolveWindow(DateTime? lastSeenUtc, DateTime now)
    {
        var fallback = now.AddDays(-FallbackWindowDays);
        if (lastSeenUtc is not { } lastSeen) return (fallback, false);

        var away = now - lastSeen;
        return away >= TimeSpan.FromHours(MinimumAwayHours) && away <= TimeSpan.FromDays(MaximumAwayDays)
            ? (lastSeen, true)
            : (fallback, false);
    }
}
