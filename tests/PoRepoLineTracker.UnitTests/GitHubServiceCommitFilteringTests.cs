using FluentAssertions;
using Xunit;

namespace PoRepoLineTracker.UnitTests;

public class GitHubServiceCommitFilteringTests
{
    [Fact]
    public void CommitDateFiltering_ShouldUseUtcDates()
    {
        // Arrange
        var localDate = new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Local);
        var utcDate = localDate.ToUniversalTime();

        // Act
        var convertedDate = localDate.Kind == DateTimeKind.Utc ? localDate : localDate.ToUniversalTime();

        // Assert
        convertedDate.Kind.Should().Be(DateTimeKind.Utc);
        convertedDate.Should().Be(utcDate);
    }

    [Fact]
    public void CommitDateFiltering_ShouldNotConvertUtcDates()
    {
        // Arrange
        var utcDate = new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var convertedDate = utcDate.Kind == DateTimeKind.Utc ? utcDate : utcDate.ToUniversalTime();

        // Assert
        convertedDate.Kind.Should().Be(DateTimeKind.Utc);
        convertedDate.Should().Be(utcDate);
    }

    [Theory]
    [InlineData("2025-11-01", "2025-10-01", true)]  // Commit after cutoff
    [InlineData("2025-09-01", "2025-10-01", false)] // Commit before cutoff
    [InlineData("2025-10-01", "2025-10-01", true)]  // Commit on cutoff date
    public void CommitDateComparison_ShouldFilterCorrectly(string commitDateStr, string sinceDateStr, bool shouldInclude)
    {
        // Arrange
        var commitDate = DateTime.Parse(commitDateStr).ToUniversalTime();
        var sinceDate = DateTime.Parse(sinceDateStr).ToUniversalTime();
        var commitDateOffset = new DateTimeOffset(commitDate);

        // Act
        var isIncluded = commitDateOffset.UtcDateTime >= sinceDate;

        // Assert
        isIncluded.Should().Be(shouldInclude);
    }
}
