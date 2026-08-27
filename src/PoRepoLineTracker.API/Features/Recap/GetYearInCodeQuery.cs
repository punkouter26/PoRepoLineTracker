using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.Recap;

/// <summary>
/// Builds one calendar year's recap.
/// </summary>
/// <param name="UserId">Whose portfolio to recap.</param>
/// <param name="Year">
/// Calendar year to report. Null means the current one — resolved here rather than by the caller
/// so "this year" is decided by the server's clock, the same clock every other window uses.
/// </param>
public record GetYearInCodeQuery(UserId UserId, int? Year) : IRequest<YearInCodeDto>;

/// <summary>
/// <para><b>Its own slice, unlike the digest.</b> The digest is the portfolio question over a
/// different window and shares the dashboard's vocabulary. A recap is a different question: every
/// boundary is a fixed calendar edge rather than a trailing window, most of its figures (peak
/// hour, weekday rhythm, language drift, biggest single commit) appear nowhere else, and once a
/// year is over its answer never changes again. Folding it into Insights would have meant one
/// handler serving two shapes with almost no overlap.</para>
///
/// <para>Everything below is arithmetic over stored commits. There is no model, no inference and
/// no scoring anywhere in this app — see CLAUDE.md.</para>
/// </summary>
public sealed class GetYearInCodeQueryHandler(
    IRepositoryDataService repositoryDataService,
    ILogger<GetYearInCodeQueryHandler> logger)
    : IRequestHandler<GetYearInCodeQuery, YearInCodeDto>
{
    /// <summary>Repositories listed on the recap's ranking card.</summary>
    private const int TopRepoCount = 5;

    /// <summary>Individual commits called out as the year's largest.</summary>
    private const int BiggestCommitCount = 3;

    /// <summary>Extensions shown on the language-drift card, ranked by how far their share moved.</summary>
    private const int DriftCount = 6;

    /// <summary>
    /// Below this many lines at BOTH endpoints, an extension is left out of the drift ranking.
    /// A file type that went from 3 lines to 9 has tripled and moves the ranking to the top on any
    /// relative measure, while saying nothing about how the year was spent.
    /// </summary>
    private const int DriftMinimumLines = 100;

    /// <summary>Hours counted as "night" for the night-owl share: 22:00–23:59 and 00:00–04:59.</summary>
    private const int NightStartHour = 22;
    private const int NightEndHour = 5;

    public async Task<YearInCodeDto> Handle(GetYearInCodeQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var year = request.Year ?? now.Year;

        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var yearEnd = yearStart.AddYears(1);

        var recap = new YearInCodeDto
        {
            Year = year,
            IsPartialYear = year == now.Year,
            CommitsByHour = new int[24].ToList(),
            CommitsByMonth = new int[12].ToList(),
            CommitsByWeekday = new int[7].ToList()
        };

        var repositories = (await repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();
        if (repositories.Count == 0)
        {
            logger.LogInformation("Recap {Year} for user {UserId}: no repositories tracked", year, request.UserId);
            return recap;
        }

        // Same parallel fan-out as the portfolio query: one Azure Table round-trip per repository,
        // serialised, is what makes an aggregate over every commit slow.
        var commitsPerRepo = await Task.WhenAll(repositories.Select(async repo =>
            (repo, commits: (await repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id)).ToList())));

        // The snapshot boundaries. "As of the start of the year" is the instant BEFORE the year
        // opened — a commit at 00:00:00 on 1 January belongs to the year, not to its baseline.
        var baselineInstant = yearStart.AddTicks(-1);

        // The closing boundary is clamped to now for the current year, so a year in progress is
        // measured against what exists today rather than against a future date at which every
        // repository would also report today's size — same number, but the intent matters if the
        // clamp is ever removed.
        var closingInstant = year == now.Year ? now : yearEnd.AddTicks(-1);

        var years = new HashSet<int>();
        var activeDates = new HashSet<DateTime>();
        var byDay = new Dictionary<DateTime, (int Commits, int LinesAdded)>();
        var startLanguages = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var endLanguages = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var topRepos = new List<RecapRepoDto>();
        var biggestCommits = new List<RecapCommitDto>();

        long linesAdded = 0, linesRemoved = 0;
        var commitsInYear = 0;

        foreach (var (repo, commits) in commitsPerRepo)
        {
            if (commits.Count == 0) continue;

            var repoCommits = 0;
            long repoLinesAdded = 0;
            var firstCommitDate = DateTime.MaxValue;

            foreach (var commit in commits)
            {
                years.Add(commit.CommitDate.Year);
                if (commit.CommitDate < firstCommitDate) firstCommitDate = commit.CommitDate;

                if (commit.CommitDate < yearStart || commit.CommitDate >= yearEnd) continue;

                repoCommits++;
                commitsInYear++;
                repoLinesAdded += commit.LinesAdded;
                linesAdded += commit.LinesAdded;
                linesRemoved += commit.LinesRemoved;

                var day = commit.CommitDate.Date;
                activeDates.Add(day);
                var entry = byDay.GetValueOrDefault(day);
                byDay[day] = (entry.Commits + 1, entry.LinesAdded + commit.LinesAdded);

                recap.CommitsByHour[commit.CommitDate.Hour]++;
                recap.CommitsByMonth[commit.CommitDate.Month - 1]++;
                recap.CommitsByWeekday[(int)commit.CommitDate.DayOfWeek]++;

                biggestCommits.Add(new RecapCommitDto
                {
                    // Short form only. Commit messages are not stored, so the SHA is the whole of
                    // a commit's printable identity and the full 40 characters would not fit a card.
                    Sha = commit.CommitSha.Length > 7 ? commit.CommitSha[..7] : commit.CommitSha,
                    Owner = repo.Owner,
                    Name = repo.Name,
                    Date = commit.CommitDate,
                    LinesAdded = commit.LinesAdded,
                    LinesRemoved = commit.LinesRemoved
                });
            }

            // Growth is a difference between two whole-repository snapshots, never the churn sum
            // above: LinesAdded counts a line written and rewritten twice as two lines, where the
            // snapshot difference is the code that actually survived the year.
            var startTotal = RepositoryTotals.TotalLinesAsOf(commits, baselineInstant);
            var endTotal = RepositoryTotals.TotalLinesAsOf(commits, closingInstant);
            recap.NetGrowth += endTotal - startTotal;

            Accumulate(startLanguages, RepositoryTotals.LinesByFileTypeAsOf(commits, baselineInstant));
            Accumulate(endLanguages, RepositoryTotals.LinesByFileTypeAsOf(commits, closingInstant));

            var startedThisYear = firstCommitDate >= yearStart && firstCommitDate < yearEnd;
            if (startedThisYear) recap.ReposStarted++;

            if (repoCommits > 0)
            {
                recap.ReposTouched++;
                topRepos.Add(new RecapRepoDto
                {
                    RepositoryId = repo.Id,
                    Owner = repo.Owner,
                    Name = repo.Name,
                    Commits = repoCommits,
                    LinesAdded = (int)Math.Min(repoLinesAdded, int.MaxValue),
                    NetGrowth = endTotal - startTotal,
                    StartedThisYear = startedThisYear
                });
            }
        }

        recap.AvailableYears = years.OrderByDescending(y => y).ToList();
        recap.Commits = commitsInYear;
        recap.LinesAdded = (int)Math.Min(linesAdded, int.MaxValue);
        recap.LinesRemoved = (int)Math.Min(linesRemoved, int.MaxValue);
        recap.HasData = commitsInYear > 0;

        if (!recap.HasData)
        {
            logger.LogInformation("Recap {Year} for user {UserId}: no commits in year", year, request.UserId);
            return recap;
        }

        recap.ActiveDays = activeDates.Count;
        recap.LongestStreakDays = CommitStreaks.Longest(activeDates);

        // The final ThenBy is not decoration. A steady year can leave every day tied on lines and
        // commits — and with only the first two keys the winner is then whatever order the
        // dictionary happened to enumerate in, which is not guaranteed and did differ between
        // runs over identical data. Breaking the tie on the date makes "your biggest day" stable,
        // and the most recent of equals is the more interesting one to be told about.
        var biggestDay = byDay
            .OrderByDescending(kv => kv.Value.LinesAdded)
            .ThenByDescending(kv => kv.Value.Commits)
            .ThenByDescending(kv => kv.Key)
            .First();
        recap.BiggestDay = new RecapDayDto
        {
            Date = biggestDay.Key,
            Commits = biggestDay.Value.Commits,
            LinesAdded = biggestDay.Value.LinesAdded
        };

        recap.TopRepos = topRepos
            .OrderByDescending(r => r.NetGrowth)
            .ThenByDescending(r => r.Commits)
            .Take(TopRepoCount)
            .ToList();

        // Tie-broken on the date for the same reason BiggestDay is, and in the same direction:
        // over a steady year every commit can be the same size, and without the second key the
        // three "largest" commits were whichever three the repository walk happened to reach
        // first. That produced a page naming late April as its biggest commits directly under a
        // biggest DAY in August — two figures over the same tie, disagreeing.
        recap.BiggestCommits = biggestCommits
            .OrderByDescending(c => c.LinesAdded)
            .ThenByDescending(c => c.Date)
            .Take(BiggestCommitCount)
            .ToList();

        recap.PeakHour = IndexOfMax(recap.CommitsByHour);

        recap.WeekdayOccurrences = CountWeekdays(yearStart, closingInstant);

        // Ranked on the FULL-PRECISION averages and rounded only for display. Rounding first
        // silently merged genuinely different weekdays: three commits over 53 Sundays is 0.0566
        // and three over 52 Mondays is 0.0577, which both round to 0.06 — the ranking then fell
        // through to the tie-break and named the wrong day, while the chart showed two bars of
        // visibly different heights beside it.
        var exactAverages = recap.CommitsByWeekday
            .Select((commits, weekday) => recap.WeekdayOccurrences[weekday] > 0
                ? (double)commits / recap.WeekdayOccurrences[weekday]
                : 0)
            .ToList();

        var peakWeekday = IndexOfMax(exactAverages);
        recap.PeakWeekday = (DayOfWeek)peakWeekday;
        recap.PeakWeekdayAverage = Math.Round(exactAverages[peakWeekday], 2);
        recap.AverageCommitsByWeekday = exactAverages.Select(a => Math.Round(a, 2)).ToList();

        var nightCommits = recap.CommitsByHour
            .Where((_, hour) => hour >= NightStartHour || hour < NightEndHour)
            .Sum();
        recap.NightOwlPercent = Math.Round((double)nightCommits / commitsInYear * 100, 1);

        recap.LanguageDrift = BuildDrift(startLanguages, endLanguages);
        recap.RisingLanguage = recap.LanguageDrift.FirstOrDefault(d => d.PercentDelta > 0)?.Extension;
        recap.FadingLanguage = recap.LanguageDrift.LastOrDefault(d => d.PercentDelta < 0)?.Extension;

        logger.LogInformation(
            "Recap {Year} for user {UserId}: {Commits} commits, {Added} lines added, {Repos} repos touched",
            year, request.UserId, recap.Commits, recap.LinesAdded, recap.ReposTouched);

        return recap;
    }

    private static void Accumulate(Dictionary<string, long> totals, IReadOnlyDictionary<string, int> breakdown)
    {
        foreach (var (extension, lines) in breakdown)
            totals[extension] = totals.GetValueOrDefault(extension) + lines;
    }

    /// <summary>
    /// Ranks extensions by how far their SHARE of the portfolio moved across the year.
    ///
    /// <para>Share rather than absolute lines, because absolute lines answer a question the
    /// Insights page already answers. A share that grew while the language itself shrank means
    /// everything else shrank faster — which is the drift worth showing, and is invisible in a
    /// line-count delta.</para>
    ///
    /// <para>Ordered by signed delta, largest gain first, so the caller can read the rising
    /// language off the front and the fading one off the back.</para>
    /// </summary>
    private static List<LanguageDriftDto> BuildDrift(Dictionary<string, long> start, Dictionary<string, long> end)
    {
        var startTotal = start.Values.Sum();
        var endTotal = end.Values.Sum();

        // A portfolio that did not exist at the start of the year has no share to have moved from.
        // Every extension would read as "+100%", which is a fact about the baseline rather than
        // about the year, so no drift is reported at all.
        if (startTotal <= 0 || endTotal <= 0) return [];

        var extensions = start.Keys.Union(end.Keys, StringComparer.OrdinalIgnoreCase);
        var drift = new List<LanguageDriftDto>();

        foreach (var extension in extensions)
        {
            var startLines = start.GetValueOrDefault(extension);
            var endLines = end.GetValueOrDefault(extension);
            if (startLines < DriftMinimumLines && endLines < DriftMinimumLines) continue;

            var startPercent = Math.Round((double)startLines / startTotal * 100, 1);
            var endPercent = Math.Round((double)endLines / endTotal * 100, 1);

            drift.Add(new LanguageDriftDto
            {
                Extension = extension,
                StartLines = (int)Math.Min(startLines, int.MaxValue),
                EndLines = (int)Math.Min(endLines, int.MaxValue),
                StartPercent = startPercent,
                EndPercent = endPercent,
                PercentDelta = Math.Round(endPercent - startPercent, 1)
            });
        }

        // Take the largest movers in EITHER direction, then re-sort signed so the list reads
        // gainers-first. Sorting signed before taking would drop every fading language.
        //
        // Rows that did not move are dropped rather than ranked last: a language holding exactly
        // its share is not drift, and including it produced a "what you drifted toward" card whose
        // every row read "70% → 70%, +0.0 pts" with an empty sentence above it, because there was
        // no rising or fading language for the sentence to name. Filtering here means the card
        // simply does not render, which is the honest answer.
        return drift
            .Where(d => d.PercentDelta != 0)
            .OrderByDescending(d => Math.Abs(d.PercentDelta))
            .Take(DriftCount)
            .OrderByDescending(d => d.PercentDelta)
            .ToList();
    }

    /// <summary>
    /// How many times each weekday fell inside the measured window, Sunday first.
    ///
    /// <para>Walked day by day rather than derived arithmetically. The closed form is only three
    /// lines but has to special-case leap years and the 52-versus-53 boundary, and this runs once
    /// per request over at most 366 iterations — the arithmetic would be optimising the wrong
    /// thing at the cost of the only part of this file a reader would have to check twice.</para>
    /// </summary>
    private static List<int> CountWeekdays(DateTime from, DateTime toInclusive)
    {
        var counts = new int[7];
        for (var day = from.Date; day <= toInclusive.Date; day = day.AddDays(1))
            counts[(int)day.DayOfWeek]++;
        return counts.ToList();
    }

    /// <summary>
    /// Index of the largest bucket, earliest on a tie. Ties are real — a user with four commits
    /// spread across four hours has no peak — and picking the earliest at least keeps the answer
    /// stable between two requests over the same data.
    /// </summary>
    private static int IndexOfMax<T>(List<T> buckets) where T : IComparable<T>
    {
        var best = 0;
        for (var i = 1; i < buckets.Count; i++)
            if (buckets[i].CompareTo(buckets[best]) > 0) best = i;
        return best;
    }
}
