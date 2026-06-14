namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Shared xUnit collection that provides a single <see cref="CustomWebApplicationFactory"/>
/// instance across all participating test classes (ICollectionFixture), eliminating the
/// per-class startup overhead of IClassFixture. The <see cref="AzuriteFixture"/> starts the
/// Testcontainers-managed Azurite container before the factory builds its host, so the factory
/// can read <see cref="AzuriteState.ConnectionString"/>.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection
    : ICollectionFixture<AzuriteFixture>, ICollectionFixture<CustomWebApplicationFactory>
{
    public const string Name = "Integration Tests";
}
