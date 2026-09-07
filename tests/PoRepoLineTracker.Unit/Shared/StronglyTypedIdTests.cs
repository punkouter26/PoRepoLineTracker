using System.Text.Json;
using FluentAssertions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The identity wrappers must buy type safety without changing anything observable:
/// the JSON the Blazor client parses and the Guid stored in Azure Table Storage are unchanged.
/// </summary>
public class StronglyTypedIdTests
{
    private sealed record Wrapper(RepositoryId RepositoryId, UserId UserId);

    [Fact]
    public void Ids_SerializeAndRoundTripThroughJson()
    {
        var guid = Guid.NewGuid();
        JsonSerializer.Serialize(new RepositoryId(guid))
            .Should().Be($"\"{guid}\"");
        JsonSerializer.Serialize(new UserId(guid)).Should().Be($"\"{guid}\"");

        var original = new Wrapper(RepositoryId.New(), UserId.New());
        var restored = JsonSerializer.Deserialize<Wrapper>(JsonSerializer.Serialize(original));
        restored.Should().Be(original);

        var legacy = $"{{\"RepositoryId\":\"{guid}\",\"UserId\":\"{Guid.Empty}\"}}";
        var restoredLegacy = JsonSerializer.Deserialize<Wrapper>(legacy);
        restoredLegacy!.RepositoryId.Value.Should().Be(guid);
    }

    [Fact]
    public void TryParse_AcceptsValidAndRejectsMalformedInput()
    {
        var guid = Guid.NewGuid();
        RepositoryId.TryParse(guid.ToString(), out var parsed).Should().BeTrue();
        parsed.Value.Should().Be(guid);

        RepositoryId.TryParse("not-a-guid", out var badRepo).Should().BeFalse();
        badRepo.Should().Be(RepositoryId.Empty);

        UserId.TryParse(null, out var badUser).Should().BeFalse();
        badUser.Should().Be(UserId.Empty);
    }
}
