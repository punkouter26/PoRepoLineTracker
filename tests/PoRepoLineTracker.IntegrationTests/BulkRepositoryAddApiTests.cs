using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Net;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models;
using Azure.Data.Tables;

namespace PoRepoLineTracker.IntegrationTests;

public class BulkRepositoryAddApiTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tableServiceClient;
    private const string TestRepositoryTableName = "PoRepoLineTrackerRepositoriesTest";
    private const string TestCommitLineCountTableName = "PoRepoLineTrackerCommitLineCountsTest";

    public BulkRepositoryAddApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;

        _client = _factory.CreateClient();

        // Setup test table service for cleanup
        _tableServiceClient = new TableServiceClient("UseDevelopmentStorage=true");
    }

    [Fact]
    public async Task AddBulkRepositories_WithValidRepositories_ShouldReturnSuccessAndStoreRepositories()
    {
        // Arrange
        await CleanupTestTables();

        var repositoriesToAdd = new List<BulkRepositoryDto>
        {
            new BulkRepositoryDto
            {
                Owner = "testowner1",
                RepoName = "testrepo1",
                CloneUrl = "https://github.com/testowner1/testrepo1.git"
            },
            new BulkRepositoryDto
            {
                Owner = "testowner2",
                RepoName = "testrepo2",
                CloneUrl = "https://github.com/testowner2/testrepo2.git"
            },
            new BulkRepositoryDto
            {
                Owner = "testowner3",
                RepoName = "testrepo3",
                CloneUrl = "https://github.com/testowner3/testrepo3.git"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", repositoriesToAdd);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var addedRepositories = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(addedRepositories);
        Assert.Equal(3, addedRepositories.Count);

        // Verify each repository was added with correct data
        foreach (var expectedRepo in repositoriesToAdd)
        {
            var actualRepo = addedRepositories.FirstOrDefault(r =>
                r.Owner == expectedRepo.Owner && r.Name == expectedRepo.RepoName);

            Assert.NotNull(actualRepo);
            Assert.Equal(expectedRepo.CloneUrl, actualRepo!.CloneUrl);
            Assert.NotEqual(Guid.Empty, actualRepo.Id);
        }

        // Verify repositories are actually stored in the database
        var getAllResponse = await _client.GetAsync("/api/repositories");
        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

        var storedRepositories = await getAllResponse.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(storedRepositories);
        Assert.Equal(3, storedRepositories.Count);
    }

    [Fact]
    public async Task AddBulkRepositories_WithEmptyList_ShouldReturnEmptyResult()
    {
        // Arrange
        await CleanupTestTables();
        var emptyRepositoryList = new List<BulkRepositoryDto>();

        // Act
        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", emptyRepositoryList);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var addedRepositories = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(addedRepositories);
        Assert.Empty(addedRepositories);
    }

    [Fact]
    public async Task AddBulkRepositories_WithDuplicateRepositories_ShouldSkipDuplicatesAndReturnExisting()
    {
        // Arrange
        await CleanupTestTables();

        var originalRepository = new BulkRepositoryDto
        {
            Owner = "duplicateowner",
            RepoName = "duplicaterepo",
            CloneUrl = "https://github.com/duplicateowner/duplicaterepo.git"
        };

        // First, add a repository
        var firstResponse = await _client.PostAsJsonAsync("/api/repositories/bulk", new[] { originalRepository });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstAddedRepos = await firstResponse.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(firstAddedRepos);
        Assert.Single(firstAddedRepos);

        // Try to add the same repository again
        var duplicateRepositories = new List<BulkRepositoryDto>
        {
            originalRepository, // Duplicate
            new BulkRepositoryDto
            {
                Owner = "newowner",
                RepoName = "newrepo",
                CloneUrl = "https://github.com/newowner/newrepo.git"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", duplicateRepositories);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var addedRepositories = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(addedRepositories);
        Assert.Equal(2, addedRepositories.Count); // Should return both existing and new

        // Verify total count in storage is 2 (original + new, not duplicate)
        var getAllResponse = await _client.GetAsync("/api/repositories");
        var allRepositories = await getAllResponse.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(allRepositories);
        Assert.Equal(2, allRepositories.Count);
    }

    [Fact]
    public async Task AddBulkRepositories_WithInvalidData_ShouldHandleGracefully()
    {
        // Arrange
        await CleanupTestTables();

        var repositoriesWithInvalidData = new List<BulkRepositoryDto>
        {
            new BulkRepositoryDto
            {
                Owner = "validowner",
                RepoName = "validrepo",
                CloneUrl = "https://github.com/validowner/validrepo.git"
            },
            new BulkRepositoryDto
            {
                Owner = "", // Invalid empty owner
                RepoName = "invalidrepo",
                CloneUrl = "https://github.com/invalidowner/invalidrepo.git"
            },
            new BulkRepositoryDto
            {
                Owner = "validowner2",
                RepoName = "", // Invalid empty repo name
                CloneUrl = "https://github.com/validowner2/invalidrepo.git"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/repositories/bulk", repositoriesWithInvalidData);

        // Assert - Should still return OK but only process valid repositories
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var addedRepositories = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(addedRepositories);

        // Should have at least the valid repository (exact count depends on validation logic)
        var validRepos = addedRepositories.Where(r =>
            !string.IsNullOrEmpty(r.Owner) && !string.IsNullOrEmpty(r.Name)).ToList();
        Assert.True(validRepos.Count > 0, "At least one valid repository should be added");
    }

    [Fact]
    public async Task GetRepositories_AfterBulkAdd_ShouldReturnAddedRepositories()
    {
        // Arrange
        await CleanupTestTables();

        var testRepositories = new List<BulkRepositoryDto>
        {
            new BulkRepositoryDto
            {
                Owner = "getowner1",
                RepoName = "getrepo1",
                CloneUrl = "https://github.com/getowner1/getrepo1.git"
            },
            new BulkRepositoryDto
            {
                Owner = "getowner2",
                RepoName = "getrepo2",
                CloneUrl = "https://github.com/getowner2/getrepo2.git"
            }
        };

        // Act - Add repositories
        var addResponse = await _client.PostAsJsonAsync("/api/repositories/bulk", testRepositories);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        // Act - Get repositories
        var getResponse = await _client.GetAsync("/api/repositories");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var repositories = await getResponse.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        Assert.NotNull(repositories);
        Assert.Equal(2, repositories.Count);

        // Verify repository details
        foreach (var expectedRepo in testRepositories)
        {
            var actualRepo = repositories.FirstOrDefault(r =>
                r.Owner == expectedRepo.Owner && r.Name == expectedRepo.RepoName);

            Assert.NotNull(actualRepo);
            Assert.Equal(expectedRepo.CloneUrl, actualRepo!.CloneUrl);
        }
    }

    private async Task CleanupTestTables()
    {
        try
        {
            await _tableServiceClient.DeleteTableAsync(TestRepositoryTableName);
        }
        catch
        {
            // Table might not exist, ignore
        }

        try
        {
            await _tableServiceClient.DeleteTableAsync(TestCommitLineCountTableName);
        }
        catch
        {
            // Table might not exist, ignore
        }
    }

    public void Dispose()
    {
        CleanupTestTables().Wait();
        _client.Dispose();
    }
}
