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

    [Fact]
    public void ClassifyCommit_AiPercentage80Plus_ReturnsAiHeavy()
    {
        var commit = CreateCommit(aiPercentage: 85, linesAdded: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("ai-heavy");
    }

    [Fact]
    public void ClassifyCommit_AiPercentage50To79_ReturnsAiModerate()
    {
        var commit = CreateCommit(aiPercentage: 60, linesAdded: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("ai-moderate");
        tags.Should().NotContain("ai-heavy");
    }

    [Fact]
    public void ClassifyCommit_AiPercentage25To49_ReturnsAiLight()
    {
        var commit = CreateCommit(aiPercentage: 30, linesAdded: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("ai-light");
        tags.Should().NotContain("ai-moderate");
        tags.Should().NotContain("ai-heavy");
    }

    [Fact]
    public void ClassifyCommit_AiPercentageBelow25_NoAiTags()
    {
        var commit = CreateCommit(aiPercentage: 10, linesAdded: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("ai-heavy");
        tags.Should().NotContain("ai-moderate");
        tags.Should().NotContain("ai-light");
    }

    [Fact]
    public void ClassifyCommit_AiBurst_LargeCommitWithHighAi()
    {
        // AI Burst: >= 100 lines added AND >= 50% AI
        var commit = CreateCommit(aiPercentage: 60, linesAdded: 150);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("ai-burst");
    }

    [Fact]
    public void ClassifyCommit_NotAiBurst_SmallCommitWithHighAi()
    {
        // Not AI Burst: < 100 lines added
        var commit = CreateCommit(aiPercentage: 80, linesAdded: 50);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("ai-burst");
    }

    [Fact]
    public void ClassifyCommit_NotAiBurst_LargeCommitWithLowAi()
    {
        // Not AI Burst: < 50% AI
        var commit = CreateCommit(aiPercentage: 30, linesAdded: 200);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("ai-burst");
    }

    #endregion

    #region Size-Based Tags

    [Fact]
    public void ClassifyCommit_HotStreak_500PlusLinesAdded()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 500);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("hot-streak");
    }

    [Fact]
    public void ClassifyCommit_NotHotStreak_Under500LinesAdded()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 499);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("hot-streak");
    }

    [Fact]
    public void ClassifyCommit_TinyCommit_FewLinesAddedAndRemoved()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 3, linesRemoved: 2);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("tiny");
    }

    [Fact]
    public void ClassifyCommit_NotTinyCommit_ManyLinesRemoved()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 3, linesRemoved: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("tiny");
    }

    [Fact]
    public void ClassifyCommit_NotTinyCommit_ManyLinesAdded()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 10, linesRemoved: 2);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("tiny");
    }

    #endregion

    #region Diff Pattern Tags

    [Fact]
    public void ClassifyCommit_BugFix_MoreRemovedThanAdded_RatioAbove2()
    {
        // Bug fix: LinesRemoved > LinesAdded AND ratio >= 2.0
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 10, linesRemoved: 25);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().Contain("bug-fix");
    }

    [Fact]
    public void ClassifyCommit_NotBugFix_RatioBelow2()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 10, linesRemoved: 15);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("bug-fix");
    }

    [Fact]
    public void ClassifyCommit_NotBugFix_MoreAddedThanRemoved()
    {
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 25, linesRemoved: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("bug-fix");
    }

    [Fact]
    public void ClassifyCommit_NotBugFix_ZeroLinesAdded()
    {
        // Bug fix requires LinesAdded > 0
        var commit = CreateCommit(aiPercentage: 0, linesAdded: 0, linesRemoved: 10);

        var tags = CommitTaggerService.ClassifyCommit(commit);

        tags.Should().NotContain("bug-fix");
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
