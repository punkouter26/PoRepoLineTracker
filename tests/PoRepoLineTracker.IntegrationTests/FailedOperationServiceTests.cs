using Xunit;
using PoRepoLineTracker.Infrastructure.Services;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PoRepoLineTracker.Application.Interfaces;

namespace PoRepoLineTracker.IntegrationTests;

[Collection("IntegrationTests")]
public class FailedOperationServiceTests : IDisposable
{
    private readonly IFailedOperationService _failedOperationService;
    private readonly TableServiceClient _tableServiceClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FailedOperationService> _logger;

    private const string TestFailedOperationTableName = "PoRepoLineTrackerFailedOperationsTest";
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";

    public FailedOperationServiceTests()
    {
        // Setup configuration for Azurite
        var inMemorySettings = new Dictionary<string, string?> {
            {"AzureTableStorage:ConnectionString", AzuriteConnectionString},
            {"AzureTableStorage:FailedOperationTableName", TestFailedOperationTableName}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Setup logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<FailedOperationService>();

        // Initialize service
        _failedOperationService = new FailedOperationService(_configuration, _logger);

        // Initialize TableServiceClient for table management
        _tableServiceClient = new TableServiceClient(AzuriteConnectionString);

        // Ensure test table is clean before tests run
        var failedOperationTableClient = _tableServiceClient.GetTableClient(TestFailedOperationTableName);
        DeleteTableIfExistsAsync(failedOperationTableClient).Wait();
        failedOperationTableClient.CreateIfNotExists();
    }

    private async Task DeleteTableIfExistsAsync(TableClient tableClient)
    {
        try
        {
            await tableClient.DeleteAsync();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Table does not exist, ignore
        }
    }

    [Fact]
    public async Task RecordAndGetFailedOperation_ShouldWorkCorrectly()
    {
        // Arrange
        var failedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitProcessing",
            EntityId = "testcommit123",
            ErrorMessage = "Test error message",
            StackTrace = "Test stack trace",
            FailedAt = DateTime.UtcNow,
            RetryCount = 0,
            ContextData = new Dictionary<string, object>
            {
                { "testKey", "testValue" }
            }
        };

        // Act
        await _failedOperationService.RecordFailedOperationAsync(failedOperation);
        var retrievedOperation = await _failedOperationService.GetFailedOperationByIdAsync(failedOperation.Id);

// Assert
        Assert.NotNull(retrievedOperation);
        Assert.Equal(failedOperation.Id, retrievedOperation.Id);
        Assert.Equal(failedOperation.RepositoryId, retrievedOperation.RepositoryId);
        Assert.Equal(failedOperation.OperationType, retrievedOperation.OperationType);
        Assert.Equal(failedOperation.EntityId, retrievedOperation.EntityId);
        Assert.Equal(failedOperation.ErrorMessage, retrievedOperation.ErrorMessage);
        Assert.Equal(failedOperation.StackTrace, retrievedOperation.StackTrace);
        Assert.Equal(failedOperation.FailedAt.ToString("yyyy-MM-dd HH:mm:ss"), retrievedOperation.FailedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        Assert.Equal(failedOperation.RetryCount, retrievedOperation.RetryCount);
        Assert.True(retrievedOperation.ContextData.ContainsKey("testKey"));
        Assert.Equal("testValue", retrievedOperation.ContextData["testKey"].ToString());
    }

    [Fact]
    public async Task GetFailedOperationsByRepositoryId_ShouldReturnCorrectOperations()
    {
        // Arrange
        var repositoryId = Guid.NewGuid();
        var failedOperation1 = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            OperationType = "CommitProcessing",
            EntityId = "commit1",
            ErrorMessage = "Error 1",
            FailedAt = DateTime.UtcNow
        };

        var failedOperation2 = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            OperationType = "CommitProcessing",
            EntityId = "commit2",
            ErrorMessage = "Error 2",
            FailedAt = DateTime.UtcNow
        };

        var differentRepositoryOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(), // Different repository
            OperationType = "CommitProcessing",
            EntityId = "commit3",
            ErrorMessage = "Error 3",
            FailedAt = DateTime.UtcNow
        };

        // Act
        await _failedOperationService.RecordFailedOperationAsync(failedOperation1);
        await _failedOperationService.RecordFailedOperationAsync(failedOperation2);
        await _failedOperationService.RecordFailedOperationAsync(differentRepositoryOperation);

        var repositoryOperations = await _failedOperationService.GetFailedOperationsByRepositoryIdAsync(repositoryId);

        // Assert
        Assert.Equal(2, repositoryOperations.Count());
        Assert.Contains(repositoryOperations, op => op.EntityId == "commit1");
        Assert.Contains(repositoryOperations, op => op.EntityId == "commit2");
        Assert.DoesNotContain(repositoryOperations, op => op.EntityId == "commit3");
    }

    [Fact]
    public async Task UpdateFailedOperation_ShouldUpdateRetryInformation()
    {
        // Arrange
        var failedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitProcessing",
            EntityId = "testcommit",
            ErrorMessage = "Initial error",
            FailedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        await _failedOperationService.RecordFailedOperationAsync(failedOperation);

        // Act
        failedOperation.RetryCount = 1;
        failedOperation.LastRetryAttempt = DateTime.UtcNow;
        await _failedOperationService.UpdateFailedOperationAsync(failedOperation);

        var updatedOperation = await _failedOperationService.GetFailedOperationByIdAsync(failedOperation.Id);

        // Assert
        Assert.NotNull(updatedOperation);
        Assert.Equal(1, updatedOperation.RetryCount);
        Assert.NotNull(updatedOperation.LastRetryAttempt);
    }

    [Fact]
    public async Task DeleteFailedOperation_ShouldRemoveOperation()
    {
        // Arrange
        var failedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitProcessing",
            EntityId = "testcommit",
            ErrorMessage = "Test error",
            FailedAt = DateTime.UtcNow
        };

        await _failedOperationService.RecordFailedOperationAsync(failedOperation);

        // Act
        await _failedOperationService.DeleteFailedOperationAsync(failedOperation.Id);

        // Assert
        var deletedOperation = await _failedOperationService.GetFailedOperationByIdAsync(failedOperation.Id);
        Assert.Null(deletedOperation);
    }

    [Fact]
    public async Task GetRetryableOperations_ShouldReturnOperationsReadyForRetry()
    {
        // Arrange
        var repositoryId = Guid.NewGuid();
        var oldFailedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            OperationType = "CommitProcessing",
            EntityId = "oldcommit",
            ErrorMessage = "Old error",
            FailedAt = DateTime.UtcNow.AddHours(-1),
            RetryCount = 0,
            LastRetryAttempt = DateTime.UtcNow.AddMinutes(-10) // Ready for retry (5 min cutoff)
        };

        var recentFailedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            OperationType = "CommitProcessing",
            EntityId = "recentcommit",
            ErrorMessage = "Recent error",
            FailedAt = DateTime.UtcNow,
            RetryCount = 0,
            LastRetryAttempt = DateTime.UtcNow.AddMinutes(-2) // Not ready for retry yet
        };

        var maxRetriedOperation = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            OperationType = "CommitProcessing",
            EntityId = "maxretriedcommit",
            ErrorMessage = "Max retried error",
            FailedAt = DateTime.UtcNow.AddHours(-2),
            RetryCount = 3 // Max retries exceeded
        };

        await _failedOperationService.RecordFailedOperationAsync(oldFailedOperation);
        await _failedOperationService.RecordFailedOperationAsync(recentFailedOperation);
        await _failedOperationService.RecordFailedOperationAsync(maxRetriedOperation);

        // Act
        var retryableOperations = await _failedOperationService.GetRetryableOperationsAsync(maxRetryCount: 3);

        // Assert
        var retryableList = retryableOperations.ToList();
        Assert.Single(retryableList);
        Assert.Equal("oldcommit", retryableList[0].EntityId);
    }

    [Fact]
    public async Task CheckConnection_ShouldVerifyStorageConnectivity()
    {
        // Act & Assert
        await _failedOperationService.CheckConnectionAsync();
        // If no exception is thrown, the connection is working
    }

    public void Dispose()
    {
        // Clean up test table after each test run
        var failedOperationTableClient = _tableServiceClient.GetTableClient(TestFailedOperationTableName);

        try { failedOperationTableClient.DeleteAsync().Wait(); } catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* Table does not exist, ignore */ }
    }
}
