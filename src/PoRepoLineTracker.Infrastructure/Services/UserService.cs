using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Infrastructure.Entities;

namespace PoRepoLineTracker.Infrastructure.Services;

/// <summary>
/// Azure Table Storage implementation of IUserService.
/// </summary>
public class UserService : IUserService
{
    private readonly TableClient _userTableClient;
    private readonly ILogger<UserService> _logger;
    private bool _tableInitialized = false;

    public UserService(TableServiceClient tableServiceClient, IConfiguration configuration, ILogger<UserService> logger)
    {
        _logger = logger;
        var tableName = configuration["AzureTableStorage:UserTableName"] ?? "PoRepoLineTrackerUsers";
        _userTableClient = tableServiceClient.GetTableClient(tableName);
    }

    private async Task EnsureTableExistsAsync()
    {
        if (!_tableInitialized)
        {
            try
            {
                await _userTableClient.CreateIfNotExistsAsync();
                _tableInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring user table exists: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        await EnsureTableExistsAsync();

        try
        {
            // Query by Id property since RowKey is GitHubId
            var query = _userTableClient.QueryAsync<UserEntity>(e => e.Id == userId);
            await foreach (var entity in query)
            {
                return entity.ToDomainModel();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID {UserId}: {ErrorMessage}", userId, ex.Message);
            throw;
        }
    }

    public async Task<User?> GetUserByGitHubIdAsync(string gitHubId)
    {
        await EnsureTableExistsAsync();

        try
        {
            var response = await _userTableClient.GetEntityIfExistsAsync<UserEntity>("USER", gitHubId);
            if (response.HasValue && response.Value is not null)
            {
                return response.Value.ToDomainModel();
            }
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by GitHub ID {GitHubId}: {ErrorMessage}", gitHubId, ex.Message);
            throw;
        }
    }

    public async Task<User> UpsertUserAsync(User user)
    {
        await EnsureTableExistsAsync();

        try
        {
            var existingUser = await GetUserByGitHubIdAsync(user.GitHubId);
            if (existingUser != null)
            {
                // Update existing user
                user.Id = existingUser.Id;
                user.CreatedAt = existingUser.CreatedAt;
                user.LastLoginAt = DateTime.UtcNow;
            }
            else
            {
                // New user
                user.Id = Guid.NewGuid();
                user.CreatedAt = DateTime.UtcNow;
                user.LastLoginAt = DateTime.UtcNow;
            }

            var entity = UserEntity.FromDomainModel(user);
            await _userTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            _logger.LogInformation("Upserted user {Username} (GitHub ID: {GitHubId})", user.Username, user.GitHubId);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting user {Username}: {ErrorMessage}", user.Username, ex.Message);
            throw;
        }
    }

    public async Task UpdateAccessTokenAsync(Guid userId, string accessToken, DateTime? expiresAt = null)
    {
        await EnsureTableExistsAsync();

        try
        {
            var user = await GetUserByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException($"User with ID {userId} not found");
            }

            user.AccessToken = accessToken;
            user.TokenExpiresAt = expiresAt;

            var entity = UserEntity.FromDomainModel(user);
            await _userTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            _logger.LogInformation("Updated access token for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating access token for user {UserId}: {ErrorMessage}", userId, ex.Message);
            throw;
        }
    }

    public async Task<string?> GetAccessTokenAsync(Guid userId)
    {
        var user = await GetUserByIdAsync(userId);
        return user?.AccessToken;
    }

    public async Task DeleteUserAsync(Guid userId, bool deleteAssociatedData = false)
    {
        await EnsureTableExistsAsync();

        try
        {
            var user = await GetUserByIdAsync(userId);
            if (user != null)
            {
                await _userTableClient.DeleteEntityAsync("USER", user.GitHubId);
                _logger.LogInformation("Deleted user {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}: {ErrorMessage}", userId, ex.Message);
            throw;
        }
    }
}
