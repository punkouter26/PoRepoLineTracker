namespace PoRepoLineTracker.Shared.Domain;

/// <summary>
/// The one definition of "how many lines does this repository have right now".
///
/// <para><b>Why this exists.</b> Two pages printed a figure under the label "Total Lines" and
/// could disagree. Insights summed each repository's most recent commit snapshot with no window;
/// the Repositories page summed the last day present in the 365-day <c>/allcharts</c> response.
/// For any repository whose last commit predates the window those are different numbers — the
/// repository still has its code, but it contributes nothing to a 365-day series, so the grid
/// showed an em dash and the portfolio total on Insights was larger than the one on the page
/// listing the repositories it was made of.</para>
///
/// <para><b>The rule.</b> <c>TotalLines</c> on a commit is a snapshot of the WHOLE repository at
/// that commit, not a delta. So the current size is the value on the newest commit — full stop,
/// no window, and never a sum across commits (which would count the same lines once per commit).
/// Everything that needs the figure calls in here.</para>
/// </summary>
public static class RepositoryTotals
{
    /// <summary>
    /// Current size of the repository: the snapshot on its newest commit, or 0 if it has none.
    /// Deliberately unwindowed — see the type remarks.
    /// </summary>
    public static int LatestTotalLines(IEnumerable<CommitLineCount>? commits)
    {
        if (commits is null) return 0;

        var newest = 0;
        DateTime? newestDate = null;

        // A single pass over an unordered sequence, rather than OrderBy().LastOrDefault(): callers
        // hold every commit of a repository and some of them have thousands.
        foreach (var commit in commits)
        {
            if (newestDate is null || commit.CommitDate > newestDate)
            {
                newestDate = commit.CommitDate;
                newest = commit.TotalLines;
            }
        }

        return newest;
    }

    /// <summary>
    /// Size as of <paramref name="asOf"/>: the snapshot on the newest commit at or before that
    /// instant, or 0 if the repository had no commits yet. This is the correct baseline for a
    /// "net change over the last N days" figure — subtracting a windowed sum would be wrong for
    /// the same reason summing snapshots is.
    /// </summary>
    public static int TotalLinesAsOf(IEnumerable<CommitLineCount>? commits, DateTime asOf)
    {
        if (commits is null) return 0;

        var value = 0;
        DateTime? bestDate = null;

        foreach (var commit in commits)
        {
            if (commit.CommitDate > asOf) continue;
            if (bestDate is null || commit.CommitDate > bestDate)
            {
                bestDate = commit.CommitDate;
                value = commit.TotalLines;
            }
        }

        return value;
    }
}
