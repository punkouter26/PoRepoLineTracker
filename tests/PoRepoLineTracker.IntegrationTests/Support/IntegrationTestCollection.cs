namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Shared xUnit collection that provides a single <see cref="CustomWebApplicationFactory"/>
/// instance across all participating test classes (ICollectionFixture), eliminating the
/// per-class startup overhead of IClassFixture.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    public const string Name = "Integration Tests";
}
