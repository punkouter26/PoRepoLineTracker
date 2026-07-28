using System.Text.Json;
using FluentAssertions;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Rule 1.5 — the identity wrappers must buy type safety without changing anything observable:
/// the JSON the Blazor client parses and the Guid stored in Azure Table Storage are unchanged.
/// </summary>
public class StronglyTypedIdTests
{
    private sealed record Wrapper(RepositoryId RepositoryId, UserId UserId);

    [Fact]
    public void RepositoryId_SerializesAsBareGuidString()
    {
        var guid = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new RepositoryId(guid));

        json.Should().Be($"\"{guid}\"", "the wire format must be identical to a raw Guid");
    }

    [Fact]
    public void UserId_SerializesAsBareGuidString()
    {
        var guid = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new UserId(guid));

        json.Should().Be($"\"{guid}\"");
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
    [InlineData("")]
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

    [Fact]
    public void ToString_MatchesUnderlyingGuid()
    {
        var guid = Guid.NewGuid();

        new RepositoryId(guid).ToString().Should().Be(guid.ToString());
        new UserId(guid).ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void New_ProducesDistinctIds()
    {
        RepositoryId.New().Should().NotBe(RepositoryId.New());
    }

    [Fact]
    public void Empty_IsTheZeroGuid()
    {
        RepositoryId.Empty.Value.Should().Be(Guid.Empty);
        UserId.Empty.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SameGuid_ProducesEqualIdsOfTheSameType()
    {
        var guid = Guid.NewGuid();

        new RepositoryId(guid).Should().Be(new RepositoryId(guid));
        new UserId(guid).Should().Be(new UserId(guid));
    }
}
