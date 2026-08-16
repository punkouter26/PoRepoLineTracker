using System.Text.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Unit;

public class CommitLineCountEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTrip()
    {
        // Deliberately Unspecified: Azure Tables rejects non-UTC DateTimes, so the mapper must
        // coerce the kind on the way in — asserted below alongside the value round-trip.
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
    }

    [Fact]
    public void ToDomainModel_EmptyJson_ReturnsEmptyDictionary()
    {
        var entity = new CommitLineCountEntity
        {
            LinesByFileTypeJson = "",
            PartitionKey = "pk",
            RowKey = "rk"
        };

        var domain = entity.ToDomainModel();

        domain.LinesByFileType.Should().BeEmpty();
    }
}

public class GitHubRepositoryEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTrip()
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
    }

    [Fact]
    public void FromDomainModel_RowKey_IsOwnerUnderscoreName()
    {
        var domain = new GitHubRepository { Owner = "myorg", Name = "myrepo", UserId = UserId.New() };
        var entity = GitHubRepositoryEntity.FromDomainModel(domain);

        entity.RowKey.Should().Be("myorg_myrepo");
        entity.PartitionKey.Should().Be(domain.UserId.ToString());
    }
}

public class UserPreferencesEntityTests
{
    [Fact]
    public void ToDomainModel_EmptyExtensions_ReturnsDefaults()
    {
        var entity = new UserPreferencesEntity
        {
            UserId = Guid.NewGuid(),
            FileExtensions = "",
            RowKey = Guid.NewGuid().ToString()
        };

        var domain = entity.ToDomainModel();

        domain.FileExtensions.Should().BeEquivalentTo(UserPreferences.DefaultFileExtensions);
    }

    [Fact]
    public void Extensions_RoundTripThroughTheCommaSeparatedColumn()
    {
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
