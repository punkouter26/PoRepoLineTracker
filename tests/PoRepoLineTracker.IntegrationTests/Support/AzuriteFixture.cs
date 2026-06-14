using Testcontainers.Azurite;
using Xunit;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Holds the Testcontainers-managed Azurite connection string for the test session.
/// <see cref="CustomWebApplicationFactory"/> reads this so the integration tier talks to a
/// real, ephemeral Azurite container (Table Storage) with zero manual cleanup.
/// </summary>
public static class AzuriteState
{
    public static string? ConnectionString { get; set; }
}

/// <summary>
/// Collection fixture that owns the Azurite container lifecycle via Testcontainers — the
/// container is started once per test session and torn down automatically (no manual
/// docker compose / cleanup). If Docker is unavailable the fixture degrades gracefully:
/// the factory then falls back to a locally-running Azurite probe, and finally to mocks.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _container = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            AzuriteState.ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            // Docker/Testcontainers unavailable — leave the connection string null so the
            // factory falls back. Never fail the whole tier just because Docker is absent.
            Console.WriteLine($"[AzuriteFixture] Testcontainers Azurite unavailable, falling back: {ex.Message}");
            AzuriteState.ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        AzuriteState.ConnectionString = null;
        try { await _container.DisposeAsync(); }
        catch { /* best-effort teardown */ }
    }
}
