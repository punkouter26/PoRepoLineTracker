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

    /// <summary>Window for the "recently" figures — movers, commit count.</summary>
    private const int RecentWindowDays = 30;

    /// <summary>Extensions shown individually in the language mix; the rest fold into "Other".</summary>
    private const int LanguageMixSize = 8;

    /// <summary>Points on the portfolio growth trend line, spaced <see cref="TrendStepDays"/> apart, newest last.</summary>
    private const int TrendPointCount = 12;
    private const int TrendStepDays = 30;

    /// <summary>Weeks of commit cadence kept per repository for the ranking table's sparkline.</summary>
    private const int SparklineWeeks = 12;

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

        // Fetched in parallel by the data service: one Azure Table round-trip per repository,
        // serialised, is what makes this page slow. Commits arrive ordered by date ascending.
        var commitsPerRepo = await repositoryDataService.GetAllRepositoriesWithCommitsAsync(request.UserId);

        var today = DateTime.UtcNow.Date;
        var recentCutoff = today.AddDays(-RecentWindowDays);
        var activityCutoff = today.AddDays(-(ActivityWindowDays - 1));

        // Newest last, so the trend chart and every sparkline read left-to-right as time passing.
        var trendDates = Enumerable.Range(0, TrendPointCount)
            .Select(i => today.AddDays(-TrendStepDays * (TrendPointCount - 1 - i)))
            .ToList();
        var trendTotals = new long[TrendPointCount];

        var movers = new List<RepositoryMovementDto>();
        var languageTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var commitsByDay = new Dictionary<DateTime, (int Commits, int LinesAdded)>();

        long recentLinesAdded = 0;
        int recentCommits = 0;

        foreach (var (repo, commits) in commitsPerRepo)
        {
            if (commits.Count == 0) continue;

            // Via RepositoryTotals so this page and the Repositories grid cannot drift: TotalLines
            // is a whole-repository snapshot taken at each commit, so "now" is the newest commit's
            // value and "30 days ago" is the newest at or before that date — never a sum, which
            // would count the same lines once per commit. `commits` is ordered, so commits[^1] is
            // the same value; the call is what documents and pins the definition.
            var latest = commits[^1];
            var latestTotal = RepositoryTotals.LatestTotalLines(commits);
            var baseline30 = RepositoryTotals.TotalLinesAsOf(commits, recentCutoff);
            var baseline7 = RepositoryTotals.TotalLinesAsOf(commits, today.AddDays(-7));

            insights.TotalLines += latestTotal;
            insights.NetLines30Days += latestTotal - baseline30;
            insights.NetLines7Days += latestTotal - baseline7;

            for (var i = 0; i < TrendPointCount; i++)
                trendTotals[i] += RepositoryTotals.TotalLinesAsOf(commits, trendDates[i]);

            foreach (var (extension, lines) in latest.LinesByFileType)
                languageTotals[extension] = languageTotals.GetValueOrDefault(extension) + lines;

            var recent = commits.Where(c => c.CommitDate >= recentCutoff).ToList();
            recentCommits += recent.Count;
            recentLinesAdded += recent.Sum(c => (long)c.LinesAdded);

            movers.Add(new RepositoryMovementDto
            {
                RepositoryId = repo.Id,
                Owner = repo.Owner,
                Name = repo.Name,
                TotalLines = latestTotal,
                NetChange30Days = latestTotal - baseline30,
                Commits30Days = recent.Count,
                WeeklyCommits = WeeklyCadence(commits, today)
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

        insights.Movers = movers.OrderByDescending(m => m.NetChange30Days).ToList();
        insights.LanguageMix = BuildLanguageMix(languageTotals);
        insights.Activity = BuildActivity(commitsByDay, activityCutoff, today);
        insights.TrendLine = trendDates
            .Select((date, i) => new PortfolioTrendPointDto { Date = date, TotalLines = (int)Math.Min(trendTotals[i], int.MaxValue) })
            .ToList();

        var activeDates = commitsByDay.Keys.ToHashSet();
        insights.ActiveDays30 = activeDates.Count(d => d >= recentCutoff);
        insights.CurrentStreakDays = CommitStreaks.Current(activeDates, today);
        insights.LongestStreakDays = CommitStreaks.Longest(activeDates);

        logger.LogInformation(
            "Portfolio insights for user {UserId}: {Repos} repos, {Lines} lines, {Commits} commits in {Days}d",
            request.UserId, insights.RepositoryCount, insights.TotalLines, insights.Commits30Days, RecentWindowDays);

        return insights;
    }

    /// <summary>
    /// Commits per week for the last <see cref="SparklineWeeks"/> weeks, oldest first. Whole
    /// 7-day buckets ending today, not calendar weeks — the sparkline compares repositories against
    /// each other, not against a shared calendar boundary that would rarely land on "today".
    /// <paramref name="commits"/> need not be filtered or ordered; every commit is checked once.
    /// </summary>
    private static List<int> WeeklyCadence(IReadOnlyList<CommitLineCount> commits, DateTime today)
    {
        var weeks = new int[SparklineWeeks];
        foreach (var commit in commits)
        {
            var daysAgo = (today - commit.CommitDate.Date).Days;
            if (daysAgo < 0 || daysAgo >= 7 * SparklineWeeks) continue;

            weeks[SparklineWeeks - 1 - daysAgo / 7]++;
        }
        return weeks.ToList();
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
}
