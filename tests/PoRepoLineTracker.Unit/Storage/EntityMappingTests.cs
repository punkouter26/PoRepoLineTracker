using System.Text.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Unit;

public class CommitLineCountEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTripAndEmptyJson()
    {
        var domain = new CommitLineCount
        {
            Id = Guid.NewGuid(),
            RepositoryId = RepositoryId.New(),
            CommitSha = "abc123def",
            CommitDate = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Unspecified),
            TotalLines = 500,
            LinesAdded = 100,
            LinesRemoved = 50,
            LinesByFileType = new Dictionary<string, int> { { ".cs", 300 }, { ".js", 200 } }
        };

        var entity = CommitLineCountEntity.FromDomainModel(domain);
        var roundTripped = entity.ToDomainModel();

        entity.CommitDate.Kind.Should().Be(DateTimeKind.Utc);
        roundTripped.Id.Should().Be(domain.Id);
        roundTripped.RepositoryId.Should().Be(domain.RepositoryId);
        roundTripped.CommitSha.Should().Be(domain.CommitSha);
        roundTripped.TotalLines.Should().Be(500);
        roundTripped.LinesAdded.Should().Be(100);
        roundTripped.LinesRemoved.Should().Be(50);
        roundTripped.LinesByFileType.Should().BeEquivalentTo(domain.LinesByFileType);

        var emptyEntity = new CommitLineCountEntity { LinesByFileTypeJson = "", PartitionKey = "pk", RowKey = "rk" };
        emptyEntity.ToDomainModel().LinesByFileType.Should().BeEmpty();
    }
}

public class GitHubRepositoryEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTripAndRowKey()
    {
        var domain = new GitHubRepository
        {
            Id = RepositoryId.New(),
            UserId = UserId.New(),
            Owner = "testowner",
            Name = "testrepo",
            CloneUrl = "https://github.com/testowner/testrepo.git",
            LastAnalyzedCommitDate = DateTime.SpecifyKind(new DateTime(2025, 3, 15), DateTimeKind.Utc)
        };

        var entity = GitHubRepositoryEntity.FromDomainModel(domain);
        var roundTripped = entity.ToDomainModel();

        roundTripped.Id.Should().Be(domain.Id);
        roundTripped.UserId.Should().Be(domain.UserId);
        roundTripped.Owner.Should().Be("testowner");
        roundTripped.Name.Should().Be("testrepo");
        roundTripped.CloneUrl.Should().Be(domain.CloneUrl);
        entity.RowKey.Should().Be("testowner_testrepo");
        entity.PartitionKey.Should().Be(domain.UserId.ToString());
    }
}

public class UserPreferencesEntityTests
{
    [Fact]
    public void Preferences_RoundTripAndEmptyExtensionsDefaults()
    {
        var empty = new UserPreferencesEntity { UserId = Guid.NewGuid(), FileExtensions = "", RowKey = Guid.NewGuid().ToString() };
        empty.ToDomainModel().FileExtensions.Should().BeEquivalentTo(UserPreferences.DefaultFileExtensions);

        var prefs = new UserPreferences
        {
            UserId = UserId.New(),
            FileExtensions = new List<string> { ".cs", ".js", ".py" },
            LastUpdated = DateTime.UtcNow
        };

        var entity = new UserPreferencesEntity(prefs);
        entity.FileExtensions.Should().Be(".cs,.js,.py");
        entity.UserId.Should().Be(prefs.UserId.Value);
        entity.ToDomainModel().FileExtensions.Should().BeEquivalentTo(prefs.FileExtensions);
    }
}
