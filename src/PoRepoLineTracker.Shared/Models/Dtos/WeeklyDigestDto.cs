using PoRepoLineTracker.Shared.Domain;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// "Here is what happened while you were away" — the figures behind the digest banner.
///
/// <para><b>Why the window is on the payload rather than assumed by the caller.</b> The server
/// picks the window: since the user's last recorded visit when that is a useful distance in the
/// past, and a trailing 7 days otherwise (see the digest query for the exact rule). The banner has
/// to label what it is showing — "since Tuesday" and "this week" are different claims — so it
/// reads the window off the response instead of hardcoding one that the server may not have
/// used.</para>
/// </summary>
public sealed class WeeklyDigestDto
{
    /// <summary>Start of the reported window, inclusive.</summary>
    public DateTime SinceUtc { get; set; }

    /// <summary>End of the reported window — the moment the digest was built.</summary>
    public DateTime UntilUtc { get; set; }

    /// <summary>
    /// True when <see cref="SinceUtc"/> is the user's last recorded visit rather than the 7-day
    /// fallback. Only this case may be labelled "since you were last here".
    /// </summary>
    public bool IsSinceLastVisit { get; set; }

    /// <summary>Last recorded visit, whether or not it was used as the window start. Null on a first visit.</summary>
    public DateTime? LastVisitUtc { get; set; }

    /// <summary>False when nothing at all happened in the window — the banner then renders nothing.</summary>
    public bool HasActivity { get; set; }

    public int Commits { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }

    /// <summary>Portfolio snapshot now minus its snapshot at <see cref="SinceUtc"/>. Negative when code shrank.</summary>
    public int NetGrowth { get; set; }

    public int ReposTouched { get; set; }
    public int ActiveDays { get; set; }

    /// <summary>Current consecutive-day commit streak, on the same definition the Insights page uses.</summary>
    public int CurrentStreakDays { get; set; }

    /// <summary>Commits in the equally-long window immediately before this one, so the banner can say "up from".</summary>
    public int PreviousCommits { get; set; }

    /// <summary>Lines added in that same preceding window.</summary>
    public int PreviousLinesAdded { get; set; }

    /// <summary>The busiest repositories of the window, most commits first.</summary>
    public List<DigestRepoDto> TopRepos { get; set; } = [];
}

/// <summary>One repository's slice of the digest window.</summary>
public sealed class DigestRepoDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Commits { get; set; }
    public int NetGrowth { get; set; }
}
