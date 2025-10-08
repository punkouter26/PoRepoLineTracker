using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PoRepoLineTracker.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class BasicFunctionalityTests : PageTest
{
    private const string BaseUrl = "http://localhost:5000";

    [Test]
    public async Task HomePage_ShouldLoadSuccessfully()
    {
        // Arrange & Act
        await Page.GotoAsync(BaseUrl);

        // Assert
        await Expect(Page).ToHaveTitleAsync("PoRepoLineTracker");
    }

    [Test]
    public async Task Navigation_ShouldContainAllPages()
    {
        // Arrange
        await Page.GotoAsync(BaseUrl);

        // Act - Wait for the page to fully load
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000); // Wait for Blazor to render navigation

        // Assert - Check that navigation links are present by looking for any links
        var repositoriesLink = Page.Locator("text=Repositories").First;
        await Expect(repositoriesLink).ToBeVisibleAsync(new() { Timeout = 10000 });

        var chartsLink = Page.Locator("text=All Charts").First;
        await Expect(chartsLink).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task RepositoriesPage_ShouldLoad()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/repositories");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Wait for Blazor to render

        // Assert - Check URL contains repositories path
        Page.Url.Should().Contain("/repositories");
    }

    [Test]
    public async Task AllChartsPage_ShouldLoad()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/allcharts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Wait for Blazor to render

        // Assert - Check URL contains allcharts path
        Page.Url.Should().Contain("/allcharts");
    }

    [Test]
    public async Task DiagnosticsPage_ShouldLoad()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/diag");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Give time for page to load

        // Assert - Check URL contains diag path
        Page.Url.Should().Contain("/diag");
    }

    [Test]
    public async Task ApiHealthEndpoint_ShouldReturnHealthy()
    {
        // Arrange
        var context = await Browser.NewContextAsync();
        var apiPage = await context.NewPageAsync();

        // Act
        var response = await apiPage.GotoAsync($"{BaseUrl}/healthz");

        // Assert
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        var content = await response.TextAsync();
        content.Should().Contain("Healthy");
        content.Should().Contain("Azure Table Storage");
        content.Should().Contain("GitHub API");

        await apiPage.CloseAsync();
        await context.CloseAsync();
    }

    [Test]
    public async Task RepositoriesApi_ShouldReturnEmptyArray()
    {
        // Arrange
        var context = await Browser.NewContextAsync();
        var apiPage = await context.NewPageAsync();

        // Act
        var response = await apiPage.GotoAsync($"{BaseUrl}/api/repositories");

        // Assert
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        var content = await response.TextAsync();
        content.Should().Be("[]");

        await apiPage.CloseAsync();
        await context.CloseAsync();
    }

    [Test]
    public async Task DirectNavigation_ShouldWorkForAllPages()
    {
        // Test direct navigation to each page by URL
        // This is more reliable than testing SPA click navigation

        // Navigate to Repositories
        await Page.GotoAsync($"{BaseUrl}/repositories");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Page.Url.Should().Contain("/repositories");

        // Navigate to All Charts
        await Page.GotoAsync($"{BaseUrl}/allcharts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Page.Url.Should().Contain("/allcharts");

        // Navigate to Diagnostics
        await Page.GotoAsync($"{BaseUrl}/diag");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Page.Url.Should().Contain("/diag");
    }

    [Test]
    public async Task BlazorApp_ShouldLoadWithoutConsoleErrors()
    {
        // Arrange
        var consoleMessages = new List<string>();
        var errors = new List<string>();

        Page.Console += (_, msg) =>
        {
            consoleMessages.Add($"{msg.Type}: {msg.Text}");
            if (msg.Type == "error")
            {
                errors.Add(msg.Text);
            }
        };

        // Act
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Wait for any delayed JS execution

        // Assert
        errors.Should().BeEmpty($"Console errors found: {string.Join(", ", errors)}");
    }

    [Test]
    public async Task StaticAssets_ShouldLoadSuccessfully()
    {
        // Arrange
        var failedRequests = new List<string>();

        Page.RequestFailed += (_, request) =>
        {
            failedRequests.Add($"{request.Method} {request.Url} - {request.Failure}");
        };

        // Act
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.Load);
        await Task.Delay(1000); // Wait for all assets to load

        // Assert
        failedRequests.Should().BeEmpty($"Failed requests: {string.Join(", ", failedRequests)}");
    }
}
