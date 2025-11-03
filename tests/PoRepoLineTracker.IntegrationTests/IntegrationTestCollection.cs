using Azure.Data.Tables;
using System.Diagnostics;
using Xunit;

namespace PoRepoLineTracker.IntegrationTests;

[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

/// <summary>
/// Base test fixture for integration tests that require Azure Table Storage.
/// Uses Azurite emulator with in-memory persistence (no Docker, no disk I/O).
/// Install: npm install -g azurite
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private Process? _azuriteProcess;
    private const int TablePort = 10002;

    /// <summary>
    /// Gets the connection string for the Azurite Table Storage instance.
    /// </summary>
    public string TableStorageConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the TableServiceClient for interacting with test table storage.
    /// </summary>
    public TableServiceClient TableServiceClient { get; private set; } = null!;

    /// <summary>
    /// Initializes the Azurite process before tests run.
    /// This ensures each test run has a clean, isolated storage environment.
    /// Uses in-memory persistence for faster test execution (no disk I/O).
    /// Requires: npm install -g azurite
    /// </summary>
    public async Task InitializeAsync()
    {
        // Start Azurite process (tables only, in-memory persistence)
        // Use full path to azurite.cmd to avoid PATH dependency in test subprocess
        _azuriteProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = @"C:\Users\punko\AppData\Roaming\npm\azurite.cmd",
                Arguments = $"--silent --inMemoryPersistence --tablePort {TablePort} --tableHost 127.0.0.1 --skipApiVersionCheck",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            _azuriteProcess.Start();
            // Wait for Azurite to start
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start Azurite. Ensure Azurite is installed globally: npm install -g azurite\n" +
                "Alternatively, install using: npm install -g azurite\n" +
                $"Error: {ex.Message}");
        }

        // Build connection string for Azurite
        TableStorageConnectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:{TablePort}/devstoreaccount1;";
        TableServiceClient = new TableServiceClient(TableStorageConnectionString);
    }

    /// <summary>
    /// Cleans up the Azurite process after tests complete.
    /// All data is automatically lost when process stops (in-memory persistence).
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_azuriteProcess != null && !_azuriteProcess.HasExited)
        {
            _azuriteProcess.Kill(entireProcessTree: true);
            _azuriteProcess.Dispose();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates a test table with the specified name.
    /// Returns the TableClient for the created table.
    /// </summary>
    /// <param name="tableName">Name of the table to create</param>
    /// <returns>TableClient for the created table</returns>
    public async Task<TableClient> CreateTestTableAsync(string tableName)
    {
        var tableClient = TableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync();
        return tableClient;
    }

    /// <summary>
    /// Deletes a test table with the specified name.
    /// Use this in test cleanup if needed.
    /// </summary>
    /// <param name="tableName">Name of the table to delete</param>
    public async Task DeleteTestTableAsync(string tableName)
    {
        var tableClient = TableServiceClient.GetTableClient(tableName);
        await tableClient.DeleteAsync();
    }
}

