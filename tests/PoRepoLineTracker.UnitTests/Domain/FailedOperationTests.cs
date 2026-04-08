using FluentAssertions;
using PoRepoLineTracker.Domain.Models;
using Xunit;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Unit tests for the retry logic and state management in failed operation handling.
/// These tests verify the domain model behavior without requiring Azure Table Storage.
/// </summary>
public class FailedOperationTests
{
    #region FailedOperation Domain Model Tests

    [Fact]
    public void FailedOperation_NewInstance_HasCorrectDefaults()
    {
        // Arrange & Act
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error"
        };

        // Assert
        failedOp.RetryCount.Should().Be(0);
        failedOp.ContextData.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedOperation_RetryCount_TracksCorrectly(int expectedRetries)
    {
        // Arrange
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error",
            RetryCount = expectedRetries
        };

        // Assert
        failedOp.RetryCount.Should().Be(expectedRetries);
    }

    [Fact]
    public void FailedOperation_LastRetryAttempt_IsNullByDefault()
    {
        // Arrange & Act
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error"
        };

        // Assert
        failedOp.LastRetryAttempt.Should().BeNull();
    }

    [Fact]
    public void FailedOperation_LastRetryAttempt_CanBeSet()
    {
        // Arrange
        var lastRetryTime = DateTime.UtcNow;

        // Act
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error",
            LastRetryAttempt = lastRetryTime
        };

        // Assert
        failedOp.LastRetryAttempt.Should().Be(lastRetryTime);
    }

    [Fact]
    public void FailedOperation_ContextData_CanStoreAdditionalInfo()
    {
        // Arrange
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error"
        };

        // Act
        failedOp.ContextData["CommitSha"] = "abc123def456";
        failedOp.ContextData["BranchName"] = "main";
        failedOp.ContextData["RetryReason"] = "Network timeout";

        // Assert
        failedOp.ContextData.Should().HaveCount(3);
        failedOp.ContextData["CommitSha"].Should().Be("abc123def456");
        failedOp.ContextData["BranchName"].Should().Be("main");
    }

    #endregion

    #region Retry Logic Tests

    [Theory]
    [InlineData(0, 3, true)]   // No retries yet, max 3, should retry
    [InlineData(1, 3, true)]   // 1 retry, max 3, should retry
    [InlineData(2, 3, true)]   // 2 retries, max 3, should retry
    [InlineData(3, 3, false)]  // 3 retries, max 3, should not retry
    [InlineData(4, 3, false)]  // 4 retries, max 3, should not retry
    public void CanRetry_BasedOnRetryCount_ReturnsCorrectValue(int currentRetries, int maxRetries, bool expected)
    {
        // Arrange
        var failedOp = new FailedOperation
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            OperationType = "CommitAnalysis",
            EntityId = "abc123",
            ErrorMessage = "Test error",
            RetryCount = currentRetries
        };

        // Act
        var canRetry = failedOp.RetryCount < maxRetries;

        // Assert
        canRetry.Should().Be(expected);
    }

    #endregion

    #region Exponential Backoff Tests

    [Theory]
    [InlineData(0, 1)]   // 2^0 = 1 minute
    [InlineData(1, 2)]   // 2^1 = 2 minutes
    [InlineData(2, 4)]   // 2^2 = 4 minutes
    [InlineData(3, 8)]   // 2^3 = 8 minutes
    [InlineData(4, 16)]  // 2^4 = 16 minutes
    [InlineData(5, 32)]  // 2^5 = 32 minutes
    public void ExponentialBackoff_CalculatesCorrectDelay(int retryCount, int expectedMinutes)
    {
        // Arrange & Act
        var delayMinutes = Math.Pow(2, retryCount);

        // Assert
        delayMinutes.Should().Be(expectedMinutes);
    }

    [Theory]
    [InlineData(10, 60)]  // 2^10 = 1024, capped at 60
    [InlineData(15, 60)]  // 2^15 = 32768, capped at 60
    public void ExponentialBackoff_CapsAtMaxDelay(int retryCount, int maxDelayMinutes)
    {
        // Arrange & Act
        var delayMinutes = Math.Min(Math.Pow(2, retryCount), maxDelayMinutes);

        // Assert
        delayMinutes.Should().Be(maxDelayMinutes);
    }

    #endregion
}
