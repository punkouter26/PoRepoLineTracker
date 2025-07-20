using Xunit;
using PoRepoLineTracker.Domain.Models;
using System;

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
        Assert.Equal(id, repo.Id);
        Assert.Equal(owner, repo.Owner);
        Assert.Equal(name, repo.Name);
        Assert.Equal(cloneUrl, repo.CloneUrl);
        Assert.Equal(lastAnalyzedCommitDate, repo.LastAnalyzedCommitDate);
    }
}
