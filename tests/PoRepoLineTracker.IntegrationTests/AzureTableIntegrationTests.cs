using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PoRepoLineTracker.IntegrationTests
{
    public class AzureTableIntegrationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        /// <summary>
        /// Runtime check: is Azurite listening on port 10002?
        /// When true, table-storage tests run; otherwise they are skipped automatically.
        /// </summary>
        private static readonly bool AzuriteAvailable = CheckAzuriteAvailable();

        private static bool CheckAzuriteAvailable()
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var task = tcp.ConnectAsync("127.0.0.1", 10002);
                return task.Wait(1500) && tcp.Connected;
            }
            catch
            {
                return false;
            }
        }

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

        [SkippableFact]
        public async Task Example_Table_Operation_With_Azurite()
        {
            Skip.IfNot(AzuriteAvailable, "Azurite is not running on port 10002 — skipping table storage test");

            var connectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
            var serviceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
            var table = serviceClient.GetTableClient("PoRepoLineTrackerIntegrationTestTable");
            await table.CreateIfNotExistsAsync();

            var entity = new Azure.Data.Tables.TableEntity("PK", System.Guid.NewGuid().ToString()) { { "Value", "abc" } };
            await table.AddEntityAsync(entity);

            var retrieved = await table.GetEntityAsync<Azure.Data.Tables.TableEntity>(entity.PartitionKey, entity.RowKey);
            var value = retrieved.Value.GetString("Value")?.ToString() ?? retrieved.Value["Value"].ToString();
            value.Should().Be("abc");

            await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
        }
    }
}