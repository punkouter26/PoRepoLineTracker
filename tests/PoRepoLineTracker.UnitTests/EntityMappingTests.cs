using System.Text.Json;
using FluentAssertions;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Infrastructure.Entities;

namespace PoRepoLineTracker.UnitTests;

public class CommitLineCountEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTrip()
    {
        var domain = new CommitLineCount
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            CommitSha = "abc123def",
            CommitDate = DateTime.SpecifyKind(new DateTime(2025, 6, 15, 10, 30, 0), DateTimeKind.Utc),
            TotalLines = 500,
            LinesAdded = 100,
            LinesRemoved = 50,
            LinesByFileType = new Dictionary<string, int> { { ".cs", 300 }, { ".js", 200 } }
        };

        var entity = CommitLineCountEntity.FromDomainModel(domain);
        var roundTripped = entity.ToDomainModel();

        roundTripped.Id.Should().Be(domain.Id);
        roundTripped.RepositoryId.Should().Be(domain.RepositoryId);
        roundTripped.CommitSha.Should().Be(domain.CommitSha);
        roundTripped.TotalLines.Should().Be(500);
        roundTripped.LinesAdded.Should().Be(100);
        roundTripped.LinesRemoved.Should().Be(50);
        roundTripped.LinesByFileType.Should().BeEquivalentTo(domain.LinesByFileType);
    }

    [Fact]
    public void FromDomainModel_HandlesUnspecifiedDateTimeKind()
    {
        var domain = new CommitLineCount
        {
            CommitDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified),
            LinesByFileType = new Dictionary<string, int>()
        };

        var entity = CommitLineCountEntity.FromDomainModel(domain);

        entity.CommitDate.Kind.Should().Be(DateTimeKind.Utc);
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

public class FailedOperationEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTrip()
    {
        var domain = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitProcessing",
            EntityId = "sha123",
            ErrorMessage = "Something went wrong",
            StackTrace = "at X.Y.Z()",
            FailedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            RetryCount = 2,
            LastRetryAttempt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            ContextData = new Dictionary<string, object> { { "LocalPath", "/tmp/repo" } }
        };

        var entity = FailedOperationEntity.FromDomainModel(domain);
        var roundTripped = entity.ToDomainModel();

        roundTripped.Id.Should().Be(domain.Id);
        roundTripped.OperationType.Should().Be("CommitProcessing");
        roundTripped.EntityId.Should().Be("sha123");
        roundTripped.ErrorMessage.Should().Be("Something went wrong");
        roundTripped.RetryCount.Should().Be(2);
    }

    [Fact]
    public void FromDomainModel_RowKey_IsOperationTypeAndEntityId()
    {
        var domain = new FailedOperation
        {
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitProcessing",
            EntityId = "sha456",
            ContextData = new Dictionary<string, object>()
        };

        var entity = FailedOperationEntity.FromDomainModel(domain);

        entity.RowKey.Should().Be("CommitProcessing_sha456");
    }
}

public class GitHubRepositoryEntityTests
{
    [Fact]
    public void FromDomainModel_ToDomainModel_RoundTrip()
    {
        var domain = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
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
        var domain = new GitHubRepository { Owner = "myorg", Name = "myrepo", UserId = Guid.NewGuid() };
        var entity = GitHubRepositoryEntity.FromDomainModel(domain);

        entity.RowKey.Should().Be("myorg_myrepo");
        entity.PartitionKey.Should().Be(domain.UserId.ToString());
    }

    [Fact]
    public void FromDomainModel_NullLastAnalyzedDate_StaysNull()
    {
        var domain = new GitHubRepository { Owner = "o", Name = "n", LastAnalyzedCommitDate = null };
        var entity = GitHubRepositoryEntity.FromDomainModel(domain);

        entity.LastAnalyzedCommitDate.Should().BeNull();
    }
}

public class TopFileEntityTests
{
    [Fact]
    public void FromDto_ToDto_RoundTrip()
    {
        var dto = new Application.Models.TopFileDto { FileName = "src/Program.cs", LineCount = 1500 };
        var repoId = Guid.NewGuid();

        var entity = TopFileEntity.FromDto(repoId, dto, 1);
        var roundTripped = entity.ToDto();

        roundTripped.FileName.Should().Be("src/Program.cs");
        roundTripped.LineCount.Should().Be(1500);
    }

    [Fact]
    public void FromDto_RowKey_IsPaddedRank()
    {
        var dto = new Application.Models.TopFileDto { FileName = "test.cs", LineCount = 100 };
        var entity = TopFileEntity.FromDto(Guid.NewGuid(), dto, 5);

        entity.RowKey.Should().Be("005");
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
    public void Constructor_SerializesExtensionsAsCommaSeparated()
    {
        var prefs = new UserPreferences
        {
            UserId = Guid.NewGuid(),
            FileExtensions = new List<string> { ".cs", ".js", ".py" },
            LastUpdated = DateTime.UtcNow
        };

        var entity = new UserPreferencesEntity(prefs);

        entity.FileExtensions.Should().Be(".cs,.js,.py");
        entity.UserId.Should().Be(prefs.UserId);
    }

    [Fact]
    public void ToDomainModel_ParsesCommaSeparatedExtensions()
    {
        var entity = new UserPreferencesEntity
        {
            UserId = Guid.NewGuid(),
            FileExtensions = ".cs,.ts,.html",
            RowKey = Guid.NewGuid().ToString()
        };

        var domain = entity.ToDomainModel();

        domain.FileExtensions.Should().BeEquivalentTo(new[] { ".cs", ".ts", ".html" });
    }
}
