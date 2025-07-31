using Xunit;

namespace PoRepoLineTracker.IntegrationTests;

[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

public class IntegrationTestFixture : IDisposable
{
    public IntegrationTestFixture()
    {
        // Setup code that runs once before any tests in the collection
    }

    public void Dispose()
    {
        // Cleanup code that runs once after all tests in the collection
    }
}
