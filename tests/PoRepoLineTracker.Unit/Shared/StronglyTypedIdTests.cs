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
    public void Ids_SerializeAsBareGuidStrings()
    {
        var guid = Guid.NewGuid();

        JsonSerializer.Serialize(new RepositoryId(guid))
            .Should().Be($"\"{guid}\"", "the wire format must be identical to a raw Guid");
        JsonSerializer.Serialize(new UserId(guid)).Should().Be($"\"{guid}\"");
    }

    [Fact]
    public void Ids_RoundTripThroughJson()
    {
        var original = new Wrapper(RepositoryId.New(), UserId.New());

        var restored = JsonSerializer.Deserialize<Wrapper>(JsonSerializer.Serialize(original));

        restored.Should().Be(original);
    }

    [Fact]
    public void RepositoryId_DeserializesFromLegacyGuidPayload()
    {
        // Payloads written before the wrapper existed must still deserialize.
        var guid = Guid.NewGuid();
        var legacy = $"{{\"RepositoryId\":\"{guid}\",\"UserId\":\"{Guid.Empty}\"}}";

        var restored = JsonSerializer.Deserialize<Wrapper>(legacy);

        restored!.RepositoryId.Value.Should().Be(guid);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData(null)]
    public void TryParse_RejectsMalformedInput(string? input)
    {
        RepositoryId.TryParse(input, out var repositoryId).Should().BeFalse();
        repositoryId.Should().Be(RepositoryId.Empty);

        UserId.TryParse(input, out var userId).Should().BeFalse();
        userId.Should().Be(UserId.Empty);
    }

    [Fact]
    public void TryParse_AcceptsGuidString()
    {
        var guid = Guid.NewGuid();

        RepositoryId.TryParse(guid.ToString(), out var parsed).Should().BeTrue();

        parsed.Value.Should().Be(guid);
    }
}
