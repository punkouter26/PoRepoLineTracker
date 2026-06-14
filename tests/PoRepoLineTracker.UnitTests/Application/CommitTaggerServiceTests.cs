using FluentAssertions;
using PoRepoLineTracker.Application.Services;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Unit tests for <see cref="CommitTaggerService"/>.
/// Verifies algorithmic commit classification based on AI percentage, line counts, and diff ratios.
/// No external dependencies — pure domain logic.
/// </summary>
public class CommitTaggerServiceTests
{
    private static CommitLineCount CreateCommit(
        double aiPercentage = 0,
        int linesAdded = 0,
        int linesRemoved = 0,
        int totalLines = 0)
    {
        return new CommitLineCount
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            CommitSha = "abc123",
            CommitDate = DateTime.UtcNow,
            TotalLines = totalLines > 0 ? totalLines : linesAdded + linesRemoved,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            AiPercentage = aiPercentage,
            LinesByFileType = new Dictionary<string, int>()
        };
    }

    #region AI-Related Tags

    // Consolidated AI-tier cases. Asserts both the expected tier AND mutual exclusivity of the
    // other tiers (preserving every assertion from the original four single-input facts).
    [Theory]
    [InlineData(85, "ai-heavy")]
    [InlineData(60, "ai-moderate")]
    [InlineData(30, "ai-light")]
    [InlineData(10, null)]        // below 25 → no AI tier
    public void ClassifyCommit_AiPercentage_ReturnsExpectedTier(double aiPercentage, string? expectedTier)
    {
        var allTiers = new[] { "ai-heavy", "ai-moderate", "ai-light" };
        var tags = CommitTaggerService.ClassifyCommit(CreateCommit(aiPercentage: aiPercentage, linesAdded: 10));

        foreach (var tier in allTiers)
        {
            if (tier == expectedTier) tags.Should().Contain(tier);
            else tags.Should().NotContain(tier);
        }
    }

    [Theory]
    [InlineData(60, 150, true)]   // AI Burst: >= 100 lines added AND >= 50% AI
    [InlineData(80, 50, false)]   // < 100 lines added
    [InlineData(30, 200, false)]  // < 50% AI
    public void ClassifyCommit_AiBurst(double aiPercentage, int linesAdded, bool expected)
    {
        var tags = CommitTaggerService.ClassifyCommit(CreateCommit(aiPercentage: aiPercentage, linesAdded: linesAdded));

        if (expected) tags.Should().Contain("ai-burst");
        else tags.Should().NotContain("ai-burst");
    }

    #endregion

    #region Size-Based Tags

    [Theory]
    [InlineData(500, true)]   // hot-streak: >= 500 lines added
    [InlineData(499, false)]
    public void ClassifyCommit_HotStreak(int linesAdded, bool expected)
    {
        var tags = CommitTaggerService.ClassifyCommit(CreateCommit(aiPercentage: 0, linesAdded: linesAdded));

        if (expected) tags.Should().Contain("hot-streak");
        else tags.Should().NotContain("hot-streak");
    }

    [Theory]
    [InlineData(3, 2, true)]    // tiny: linesAdded <= 5 AND linesRemoved <= 5
    [InlineData(3, 10, false)]  // too many removed
    [InlineData(10, 2, false)]  // too many added
    public void ClassifyCommit_Tiny(int linesAdded, int linesRemoved, bool expected)
    {
        var tags = CommitTaggerService.ClassifyCommit(CreateCommit(aiPercentage: 0, linesAdded: linesAdded, linesRemoved: linesRemoved));

        if (expected) tags.Should().Contain("tiny");
        else tags.Should().NotContain("tiny");
    }

    #endregion

    #region Diff Pattern Tags

    [Theory]
    [InlineData(10, 25, true)]   // bug-fix: LinesRemoved > LinesAdded AND ratio >= 2.0 AND added > 0
    [InlineData(10, 15, false)]  // ratio < 2.0
    [InlineData(25, 10, false)]  // more added than removed
    [InlineData(0, 10, false)]   // requires LinesAdded > 0
    public void ClassifyCommit_BugFix(int linesAdded, int linesRemoved, bool expected)
    {
        var tags = CommitTaggerService.ClassifyCommit(CreateCommit(aiPercentage: 0, linesAdded: linesAdded, linesRemoved: linesRemoved));

        if (expected) tags.Should().Contain("bug-fix");
        else tags.Should().NotContain("bug-fix");
    }

    #endregion

    #region Combined Tags

    [Fact]
    public void ClassifyCommit_AiHeavyAiBurstHotStreak_AllTagsPresent()
    {
        // Extreme commit: high AI, large, burst
        var commit = CreateCommit(aiPercentage: 90, linesAdded: 600, linesRemoved: 50);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("ai-heavy");
        tags.Should().Contain("ai-burst");
        tags.Should().Contain("hot-streak");
    }

    [Fact]
    public void ClassifyCommit_TinyBugFix_BothTagsPresent()
    {
        // tiny: linesAdded <= 5 AND linesRemoved <= 5
        // bug-fix: linesRemoved > linesAdded AND ratio >= 2.0
        // With linesAdded=2, linesRemoved=5: tiny=yes (both <=5), bug-fix=no (ratio=2.5 but need removed > added)
        // Actually bug-fix requires linesRemoved > linesAdded, so 5 > 2 = true, ratio = 5/2 = 2.5 >= 2.0 = true
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 2, linesRemoved: 5);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("tiny");
        tags.Should().Contain("bug-fix");
    }

    [Fact]
    public void ClassifyCommit_ZeroLinesAddedAndRemoved_ReturnsTiny()
    {
        // linesAdded=0 <= 5 AND linesRemoved=0 <= 5 => tiny tag applies
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 0, linesRemoved: 0);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("tiny");
        tags.Should().HaveCount(1, "only tiny should be present for zero-line commits");
    }

    #endregion
}
