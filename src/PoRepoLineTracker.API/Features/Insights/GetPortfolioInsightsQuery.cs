using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.Insights;

/// <summary>
/// Aggregates every tracked repository into the single figure set the Insights dashboard renders.
/// </summary>
public record GetPortfolioInsightsQuery(UserId UserId) : IRequest<PortfolioInsightsDto>;

public sealed class GetPortfolioInsightsQueryHandler(
    IRepositoryDataService repositoryDataService,
    ILogger<GetPortfolioInsightsQueryHandler> logger)
    : IRequestHandler<GetPortfolioInsightsQuery, PortfolioInsightsDto>
{
    /// <summary>Span of the activity heatmap, and the window the streak figures search.</summary>
    private const int ActivityWindowDays = 365;

    /// <summary>Window for the "recently" figures — movers, commit count, AI share.</summary>
    private const int RecentWindowDays = 30;

    /// <summary>Extensions shown individually in the language mix; the rest fold into "Other".</summary>
    private const int LanguageMixSize = 8;

    public async Task<PortfolioInsightsDto> Handle(GetPortfolioInsightsQuery request, CancellationToken cancellationToken)
    {
        var repositories = (await repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();

        var insights = new PortfolioInsightsDto
        {
            RepositoryCount = repositories.Count,
            AnalyzedCount = repositories.Count(r => r.LastAnalyzedCommitDate.HasValue)
        };

        if (repositories.Count == 0)
        {
            logger.LogInformation("Portfolio insights for user {UserId}: no repositories tracked", request.UserId);
            return insights;
        }

        // Fetched in parallel for the same reason GetAllRepositoriesLineCountHistory does it:
        // one Azure Table round-trip per repository, serialised, is what makes this page slow.
        var commitsPerRepo = await Task.WhenAll(repositories.Select(async repo =>
            (repo, commits: (await repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id))
                .OrderBy(c => c.CommitDate)
                .ToList())));

        var today = DateTime.UtcNow.Date;
        var recentCutoff = today.AddDays(-RecentWindowDays);
        var activityCutoff = today.AddDays(-(ActivityWindowDays - 1));

        var movers = new List<RepositoryMovementDto>();
        var languageTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var commitsByDay = new Dictionary<DateTime, (int Commits, int LinesAdded)>();

        double weightedAiLines = 0;
        long recentLinesAdded = 0;
        int recentCommits = 0;

        foreach (var (repo, commits) in commitsPerRepo)
        {
            if (commits.Count == 0) continue;

            // TotalLines is a whole-repository snapshot taken at each commit, so "now" is the last
            // commit's value and "30 days ago" is the last commit at or before that date — not a
            // sum, which would count the same lines once per commit.
            var latest = commits[^1];
            var baseline30 = LastSnapshotOnOrBefore(commits, recentCutoff);
            var baseline7 = LastSnapshotOnOrBefore(commits, today.AddDays(-7));

            insights.TotalLines += latest.TotalLines;
            insights.NetLines30Days += latest.TotalLines - baseline30;
            insights.NetLines7Days += latest.TotalLines - baseline7;

            foreach (var (extension, lines) in latest.LinesByFileType)
                languageTotals[extension] = languageTotals.GetValueOrDefault(extension) + lines;

            var recent = commits.Where(c => c.CommitDate >= recentCutoff).ToList();
            recentCommits += recent.Count;
            recentLinesAdded += recent.Sum(c => (long)c.LinesAdded);
            weightedAiLines += recent.Sum(c => c.LinesAdded * Math.Clamp(c.AiPercentage, 0, 100) / 100.0);

            movers.Add(new RepositoryMovementDto
            {
                RepositoryId = repo.Id,
                Owner = repo.Owner,
                Name = repo.Name,
                TotalLines = latest.TotalLines,
                NetChange30Days = latest.TotalLines - baseline30,
                Commits30Days = recent.Count,
                AiPercentage30Days = WeightedAiShare(recent)
            });

            foreach (var commit in commits.Where(c => c.CommitDate.Date >= activityCutoff))
            {
                var day = commit.CommitDate.Date;
                var existing = commitsByDay.GetValueOrDefault(day);
                commitsByDay[day] = (existing.Commits + 1, existing.LinesAdded + commit.LinesAdded);
            }
        }

        insights.Commits30Days = recentCommits;
        insights.LinesAdded30Days = (int)Math.Min(recentLinesAdded, int.MaxValue);
        insights.AiPercentage30Days = recentLinesAdded > 0
            ? Math.Round(weightedAiLines / recentLinesAdded * 100, 1)
            : 0;

        insights.Movers = movers.OrderByDescending(m => m.NetChange30Days).ToList();
        insights.LanguageMix = BuildLanguageMix(languageTotals);
        insights.Activity = BuildActivity(commitsByDay, activityCutoff, today);

        var activeDates = commitsByDay.Keys.ToHashSet();
        insights.ActiveDays30 = activeDates.Count(d => d >= recentCutoff);
        insights.CurrentStreakDays = CurrentStreak(activeDates, today);
        insights.LongestStreakDays = LongestStreak(activeDates);

        logger.LogInformation(
            "Portfolio insights for user {UserId}: {Repos} repos, {Lines} lines, {Commits} commits in {Days}d",
            request.UserId, insights.RepositoryCount, insights.TotalLines, insights.Commits30Days, RecentWindowDays);

        return insights;
    }

    /// <summary>
    /// The repository's line count as of <paramref name="cutoff"/>.
    /// <para>
    /// A repository whose history starts inside the window has no snapshot that old; its baseline
    /// is 0 so its whole size reads as growth, which is the truthful answer to "how much did this
    /// add in 30 days" for something that did not exist 30 days ago.
    /// </para>
    /// <paramref name="commits"/> must be ordered by date ascending.
    /// </summary>
    private static int LastSnapshotOnOrBefore(List<CommitLineCount> commits, DateTime cutoff)
    {
        var baseline = 0;
        foreach (var commit in commits)
        {
            if (commit.CommitDate > cutoff) break;
            baseline = commit.TotalLines;
        }
        return baseline;
    }

    private static double WeightedAiShare(List<CommitLineCount> commits)
    {
        var added = commits.Sum(c => (double)c.LinesAdded);
        if (added <= 0) return 0;

        var ai = commits.Sum(c => c.LinesAdded * Math.Clamp(c.AiPercentage, 0, 100) / 100.0);
        return Math.Round(ai / added * 100, 1);
    }

    private static List<LanguageShareDto> BuildLanguageMix(Dictionary<string, long> totals)
    {
        var grandTotal = totals.Values.Sum();
        if (grandTotal <= 0) return [];

        var ranked = totals.OrderByDescending(kv => kv.Value).ToList();
        var mix = ranked.Take(LanguageMixSize)
            .Select(kv => new LanguageShareDto
            {
                Extension = kv.Key,
                Lines = (int)Math.Min(kv.Value, int.MaxValue),
                Percentage = Math.Round((double)kv.Value / grandTotal * 100, 1)
            })
            .ToList();

        var remainder = ranked.Skip(LanguageMixSize).Sum(kv => kv.Value);
        if (remainder > 0)
        {
            mix.Add(new LanguageShareDto
            {
                Extension = "Other",
                Lines = (int)Math.Min(remainder, int.MaxValue),
                Percentage = Math.Round((double)remainder / grandTotal * 100, 1)
            });
        }

        return mix;
    }

    /// <summary>
    /// One entry per calendar day across the window, zero-commit days included. The heatmap draws a
    /// fixed grid, so a sparse list would silently shift every cell after the first gap.
    /// </summary>
    private static List<ActivityDayDto> BuildActivity(
        Dictionary<DateTime, (int Commits, int LinesAdded)> byDay, DateTime from, DateTime to)
    {
        var days = new List<ActivityDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var entry = byDay.GetValueOrDefault(date);
            days.Add(new ActivityDayDto { Date = date, Commits = entry.Commits, LinesAdded = entry.LinesAdded });
        }
        return days;
    }

    /// <summary>
    /// Consecutive active days ending today — or ending yesterday, since a day with no commits yet
    /// should not read as having broken a streak before it is over.
    /// </summary>
    private static int CurrentStreak(HashSet<DateTime> activeDates, DateTime today)
    {
        var cursor = activeDates.Contains(today) ? today : today.AddDays(-1);

        var streak = 0;
        while (activeDates.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    private static int LongestStreak(HashSet<DateTime> activeDates)
    {
        var longest = 0;
        foreach (var date in activeDates)
        {
            // Only count from the start of a run, so each run is walked once rather than once per
            // day it contains.
            if (activeDates.Contains(date.AddDays(-1))) continue;

            var length = 0;
            for (var cursor = date; activeDates.Contains(cursor); cursor = cursor.AddDays(1))
                length++;

            if (length > longest) longest = length;
        }
        return longest;
    }
}
