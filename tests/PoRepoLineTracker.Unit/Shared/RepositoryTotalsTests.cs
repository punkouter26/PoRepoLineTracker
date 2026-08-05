using FluentAssertions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// <see cref="RepositoryTotals"/> is the single definition of "how many lines does this repository
/// have right now", extracted because the Repositories page and the Insights page had each grown
/// their own and could print different numbers under the same label.
///
/// <para>These pin the two properties that made them disagree: the figure is a SNAPSHOT (the value
/// on the newest commit, never a sum), and it is UNWINDOWED (a repository untouched for years
/// still has its code).</para>
/// </summary>
public class RepositoryTotalsTests
{
    private static readonly DateTime Day0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CommitLineCount At(int day, int totalLines) => new()
    {
        Id = Guid.NewGuid(),
        CommitDate = Day0.AddDays(day),
        TotalLines = totalLines
    };

    // ─── LatestTotalLines ────────────────────────────────────────────────────

    [Fact]
    public void LatestTotalLines_TakesTheNewestSnapshot_NotTheSum()
    {
        var commits = new[] { At(0, 100), At(1, 250), At(2, 400) };

        RepositoryTotals.LatestTotalLines(commits).Should().Be(400);
    }

    /// <summary>
    /// Callers pass whatever the storage layer handed back, and Azure Table queries carry no
    /// ordering guarantee. Depending on sequence order here would make the figure depend on how
    /// the rows happened to come out of the table.
    /// </summary>
    [Fact]
    public void LatestTotalLines_DoesNotAssumeTheSequenceIsOrdered()
    {
        var commits = new[] { At(2, 400), At(0, 100), At(1, 250) };

        RepositoryTotals.LatestTotalLines(commits).Should().Be(400);
    }

    [Fact]
    public void LatestTotalLines_IsTheNewestValueEvenWhenTheRepositoryShrank()
    {
        var commits = new[] { At(0, 5_000), At(1, 1_200) };

        RepositoryTotals.LatestTotalLines(commits).Should().Be(1_200);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LatestTotalLines_IsZeroWithNothingToReadFrom(bool useNull)
    {
        var commits = useNull ? null : Array.Empty<CommitLineCount>();

        RepositoryTotals.LatestTotalLines(commits).Should().Be(0);
    }

    /// <summary>The behaviour the windowed derivation got wrong — age is not relevance.</summary>
    [Fact]
    public void LatestTotalLines_IgnoresHowOldTheNewestCommitIs()
    {
        var commits = new[] { At(-900, 7_500) };

        RepositoryTotals.LatestTotalLines(commits).Should().Be(7_500);
    }

    // ─── TotalLinesAsOf ──────────────────────────────────────────────────────

    [Fact]
    public void TotalLinesAsOf_TakesTheNewestSnapshotAtOrBeforeTheInstant()
    {
        var commits = new[] { At(0, 100), At(5, 500), At(10, 900) };

        RepositoryTotals.TotalLinesAsOf(commits, Day0.AddDays(7)).Should().Be(500);
    }

    [Fact]
    public void TotalLinesAsOf_IncludesACommitExactlyOnTheBoundary()
    {
        var commits = new[] { At(0, 100), At(5, 500) };

        RepositoryTotals.TotalLinesAsOf(commits, Day0.AddDays(5)).Should().Be(500);
    }

    /// <summary>
    /// Zero, not the earliest snapshot. A repository that did not exist at the baseline instant
    /// should have its whole current size read as growth over the window.
    /// </summary>
    [Fact]
    public void TotalLinesAsOf_IsZeroWhenEveryCommitIsNewerThanTheInstant()
    {
        var commits = new[] { At(10, 900), At(20, 1_500) };

        RepositoryTotals.TotalLinesAsOf(commits, Day0).Should().Be(0);
    }

    [Fact]
    public void TotalLinesAsOf_DoesNotAssumeTheSequenceIsOrdered()
    {
        var commits = new[] { At(10, 900), At(0, 100), At(5, 500) };

        RepositoryTotals.TotalLinesAsOf(commits, Day0.AddDays(7)).Should().Be(500);
    }

    [Fact]
    public void TotalLinesAsOf_IsZeroWithNothingToReadFrom()
    {
        RepositoryTotals.TotalLinesAsOf(null, Day0).Should().Be(0);
        RepositoryTotals.TotalLinesAsOf([], Day0).Should().Be(0);
    }

    // ─── The two together ────────────────────────────────────────────────────

    /// <summary>
    /// Net change over a window is latest minus baseline. Stated as a test because the tempting
    /// wrong answer — summing LinesAdded less LinesRemoved across the window — drifts from the
    /// snapshots whenever a commit is rewritten, squashed or force-pushed.
    /// </summary>
    [Fact]
    public void NetChange_IsTheDifferenceBetweenTwoSnapshots()
    {
        var commits = new[] { At(0, 1_000), At(5, 1_100), At(10, 1_450) };

        var net = RepositoryTotals.LatestTotalLines(commits)
                  - RepositoryTotals.TotalLinesAsOf(commits, Day0.AddDays(3));

        net.Should().Be(450);
    }
}
