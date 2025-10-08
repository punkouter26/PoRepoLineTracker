using Xunit;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Integration tests to verify connectivity to Azure resources.
/// These tests use Azurite locally and can be configured to test against real Azure resources.
/// </summary>
[Collection("IntegrationTests")]
public class AzureResourceConnectivityTests
{
    private readonly IConfiguration _configuration;

    public AzureResourceConnectivityTests()
    {
        // Load configuration from appsettings.Development.json
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    [Fact]
    public async Task AzureTableStorage_ShouldConnect_Successfully()
    {
        // Arrange
        var connectionString = _configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var testTableName = $"ConnectivityTest{Guid.NewGuid():N}";

        // Act & Assert
        var tableServiceClient = new TableServiceClient(connectionString);
        var tableClient = tableServiceClient.GetTableClient(testTableName);

        // Create table to verify write access
        await tableClient.CreateIfNotExistsAsync();
        Assert.NotNull(tableClient);

        // Verify table exists
        var exists = await TableExistsAsync(tableClient);
        Assert.True(exists, "Table should exist after creation");

        // Cleanup
        await tableClient.DeleteAsync();
    }

    [Fact]
    public async Task AzureTableStorage_Repositories_ShouldCreateAndQuery_Successfully()
    {
        // Arrange
        var connectionString = _configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var tableName = _configuration["AzureTableStorage:RepositoryTableName"] ?? "PoRepoLineTrackerRepositories";
        var tableClient = new TableServiceClient(connectionString).GetTableClient(tableName);

        // Act
        await tableClient.CreateIfNotExistsAsync();

        // Assert - Verify we can query the table (even if empty)
        var queryResult = tableClient.QueryAsync<TableEntity>(maxPerPage: 1);
        await foreach (var _ in queryResult)
        {
            // Successfully queried
            break;
        }

        Assert.True(true, "Successfully connected and queried repositories table");
    }

    [Fact]
    public async Task AzureTableStorage_CommitLineCounts_ShouldCreateAndQuery_Successfully()
    {
        // Arrange
        var connectionString = _configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var tableName = _configuration["AzureTableStorage:CommitLineCountTableName"] ?? "PoRepoLineTrackerCommitLineCounts";
        var tableClient = new TableServiceClient(connectionString).GetTableClient(tableName);

        // Act
        await tableClient.CreateIfNotExistsAsync();

        // Assert - Verify we can query the table (even if empty)
        var queryResult = tableClient.QueryAsync<TableEntity>(maxPerPage: 1);
        await foreach (var _ in queryResult)
        {
            // Successfully queried
            break;
        }

        Assert.True(true, "Successfully connected and queried commit line counts table");
    }

    [Fact]
    public async Task AzureTableStorage_FailedOperations_ShouldCreateAndQuery_Successfully()
    {
        // Arrange
        var connectionString = _configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var tableName = _configuration["AzureTableStorage:FailedOperationTableName"] ?? "PoRepoLineTrackerFailedOperations";
        var tableClient = new TableServiceClient(connectionString).GetTableClient(tableName);

        // Act
        await tableClient.CreateIfNotExistsAsync();

        // Assert - Verify we can query the table (even if empty)
        var queryResult = tableClient.QueryAsync<TableEntity>(maxPerPage: 1);
        await foreach (var _ in queryResult)
        {
            // Successfully queried
            break;
        }

        Assert.True(true, "Successfully connected and queried failed operations table");
    }

    [Fact]
    public async Task AzureTableStorage_ShouldPerformCRUDOperations_Successfully()
    {
        // Arrange
        var connectionString = _configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var testTableName = $"CRUDTest{Guid.NewGuid():N}";
        var tableServiceClient = new TableServiceClient(connectionString);
        var tableClient = tableServiceClient.GetTableClient(testTableName);

        try
        {
            await tableClient.CreateIfNotExistsAsync();

            var testEntity = new TableEntity("TestPartition", "TestRow")
            {
                { "TestProperty", "TestValue" }
            };

            // Create
            await tableClient.AddEntityAsync(testEntity);

            // Read
            var retrievedEntity = await tableClient.GetEntityAsync<TableEntity>("TestPartition", "TestRow");
            Assert.Equal("TestValue", retrievedEntity.Value["TestProperty"]);

            // Update
            testEntity["TestProperty"] = "UpdatedValue";
            await tableClient.UpdateEntityAsync(testEntity, retrievedEntity.Value.ETag);

            // Verify Update
            retrievedEntity = await tableClient.GetEntityAsync<TableEntity>("TestPartition", "TestRow");
            Assert.Equal("UpdatedValue", retrievedEntity.Value["TestProperty"]);

            // Delete
            await tableClient.DeleteEntityAsync("TestPartition", "TestRow");

            // Verify Delete
            await Assert.ThrowsAsync<Azure.RequestFailedException>(async () =>
                await tableClient.GetEntityAsync<TableEntity>("TestPartition", "TestRow"));
        }
        finally
        {
            // Cleanup
            await tableClient.DeleteAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(TableClient tableClient)
    {
        try
        {
            await tableClient.GetAccessPoliciesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
