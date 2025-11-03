using FluentAssertions;
using PoRepoLineTracker.Domain.Models;
using Xunit;

namespace PoRepoLineTracker.UnitTests;

public class GitHubRepositoryTests
{
    [Fact]
    public void GitHubRepository_Properties_AreSetCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var owner = "testowner";
        var name = "testrepo";
        var cloneUrl = "https://github.com/testowner/testrepo.git";
        var lastAnalyzedCommitDate = DateTime.UtcNow;

        // Act
        var repo = new GitHubRepository
        {
            Id = id,
            Owner = owner,
            Name = name,
            CloneUrl = cloneUrl,
            LastAnalyzedCommitDate = lastAnalyzedCommitDate
        };

        // Assert
        repo.Id.Should().Be(id);
        repo.Owner.Should().Be(owner);
        repo.Name.Should().Be(name);
        repo.CloneUrl.Should().Be(cloneUrl);
        repo.LastAnalyzedCommitDate.Should().Be(lastAnalyzedCommitDate);
    }
}

