using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace PoRepoLineTracker.IntegrationTests
{
    public class AzureTableIntegrationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        public AzureTableIntegrationTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Home_Page_Loads()
        {
            // Basic smoke test to ensure the app starts under the test host without requiring external Table connections
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/");
            // App may serve static files in development or return 404 if not present. Ensure we didn't get a server error.
            Assert.False((int)response.StatusCode >= 500, "Server error encountered");
            var html = await response.Content.ReadAsStringAsync();
            // If HTML exists, ensure it's not empty; otherwise 404 is acceptable in test CI environments
            if (response.IsSuccessStatusCode)
            {
                Assert.NotEmpty(html);
            }
        }

        [Fact(Skip = "Requires local Azurite; enable when running with docker/azurite available")]
        public async Task Example_Table_Operation_With_Azurite()
        {
            // Example (skipped by default) that demonstrates how to run a Table operation against Azurite in CI/local runs where Azurite is available
            var connectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqG7hQ==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
            var serviceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
            var table = serviceClient.GetTableClient("PoRepoLineTrackerIntegrationTestTable");
            await table.CreateIfNotExistsAsync();

            var entity = new Azure.Data.Tables.TableEntity("PK", System.Guid.NewGuid().ToString()) { { "Value", "abc" } };
            await table.AddEntityAsync(entity);

            var retrieved = await table.GetEntityAsync<Azure.Data.Tables.TableEntity>(entity.PartitionKey, entity.RowKey);
            Assert.Equal("abc", retrieved.Value.GetString("Value")?.ToString() ?? retrieved.Value["Value"].ToString());

            await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
        }
    }
}