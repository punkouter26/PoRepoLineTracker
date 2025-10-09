using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PoRepoLineTracker.E2ETests;

/// <summary>
/// E2E tests for adding the PoDebateRap repository and verifying chart visualization.
/// 
/// Repository: https://github.com/punkouter26/PoDebateRap
/// Purpose: Verify that after adding a private repository, the chart displays visual data correctly.
/// 
/// Prerequisites:
/// - GitHub PAT configured in appsettings.Development.json
/// - API must be running on http://localhost:5000
/// - Repository must be accessible with the configured PAT
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AddPoDebateRapTests : PageTest
{
    private const string BaseUrl = "http://localhost:5000";
    private const string HomePage = $"{BaseUrl}/";
    private const string RepositoriesPage = $"{BaseUrl}/repositories";
    private const string RepositoryUrl = "https://github.com/punkouter26/PoDebateRap";
    private const string RepositoryName = "PoDebateRap";

    [SetUp]
    public async Task Setup()
    {
        // Start from home page
        await Page.GotoAsync(HomePage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Wait for Blazor to render
    }

    [Test]
    [Order(1)]
    public async Task Should_AddPoDebateRapRepository_Successfully()
    {
        // Arrange - Go to home page with add repository form
        await Page.GotoAsync(HomePage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Check if repository already exists
        var existingRepoLink = Page.Locator($"a:has-text('{RepositoryName}')");
        if (await existingRepoLink.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            // Repository already exists, skip to verification
            Assert.Pass("Repository already exists - skipping add");
            return;
        }

        // Act - Fill in the form
        var urlInput = Page.Locator("input[placeholder*='repository URL']");
        await Expect(urlInput).ToBeVisibleAsync(new() { Timeout = 10000 });
        await urlInput.FillAsync(RepositoryUrl);

        // Click Add Repository button
        var addButton = Page.Locator("button:has-text('Add Repository')");
        await addButton.ClickAsync();

        // Wait for processing - look for success indicators
        await Task.Delay(3000); // Initial wait for processing to start

        // Assert - Repository should appear in the list
        var repoLink = Page.Locator($"a:has-text('{RepositoryName}')");
        
        // Wait up to 60 seconds for the repository to be analyzed
        var retries = 0;
        var maxRetries = 20;
        var found = false;
        
        while (retries < maxRetries && !found)
        {
            await Page.ReloadAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(2000);
            
            found = await repoLink.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false);
            if (!found)
            {
                retries++;
                await Task.Delay(3000);
            }
        }

        await Expect(repoLink).ToBeVisibleAsync(new() { Timeout = 5000 });
        
        // Verify the status shows as ANALYZED
        var statusBadge = Page.Locator($"text={RepositoryName}").Locator("xpath=ancestor::div[contains(@class, 'card')]//span:has-text('ANALYZED')");
        await Expect(statusBadge).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    [Order(2)]
    public async Task Should_NavigateToPoDebateRapRepository_AndShowChart()
    {
        // Arrange - Navigate to repositories page
        await Page.GotoAsync(RepositoriesPage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Act - Click on the repository link
        var repoLink = Page.Locator($"a:has-text('{RepositoryName}')");
        await Expect(repoLink).ToBeVisibleAsync(new() { Timeout = 10000 });
        await repoLink.ClickAsync();

        // Wait for navigation and chart to load
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000); // Wait for chart data to load

        // Assert - Chart container should be visible
        var chartContainer = Page.Locator(".rz-chart");
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    [Test]
    [Order(3)]
    public async Task Chart_ShouldHaveWhiteBackground()
    {
        // Arrange - Navigate to repository and ensure chart is visible
        await NavigateToRepositoryChart();

        // Act - Get chart container
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Background should be white or light colored (not black)
        var backgroundColor = await chartContainer.EvaluateAsync<string>(
            "element => window.getComputedStyle(element).backgroundColor"
        );

        backgroundColor.Should().NotBeNullOrEmpty();
        // Should be white (rgb(255, 255, 255)) or similar light color
        backgroundColor.Should().Match(bg => 
            bg.Contains("255, 255, 255") || 
            bg.Contains("rgb(255") ||
            bg.Contains("white"),
            "because chart background should be white or light colored");
    }

    [Test]
    [Order(4)]
    public async Task Chart_ShouldContainSVGElement()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Locate SVG element inside chart
        var svgElement = Page.Locator(".rz-chart svg").First;

        // Assert - SVG should exist and be visible
        await Expect(svgElement).ToBeVisibleAsync(new() { Timeout = 15000 });
        
        // SVG should have width and height
        var svgBox = await svgElement.BoundingBoxAsync();
        svgBox.Should().NotBeNull();
        svgBox!.Width.Should().BeGreaterThan(0);
        svgBox.Height.Should().BeGreaterThan(0);
    }

    [Test]
    [Order(5)]
    public async Task Chart_ShouldContainVisualDataElements()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Look for chart data visualization elements (paths, circles, lines)
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Check for SVG path elements (line charts use path for the line)
        var pathElements = Page.Locator(".rz-chart svg path");
        var pathCount = await pathElements.CountAsync();

        // Check for SVG circle elements (data points)
        var circleElements = Page.Locator(".rz-chart svg circle");
        var circleCount = await circleElements.CountAsync();

        // Check for polyline elements (alternative line rendering)
        var polylineElements = Page.Locator(".rz-chart svg polyline");
        var polylineCount = await polylineElements.CountAsync();

        // Assert - Should have at least one visual element
        var totalVisualElements = pathCount + circleCount + polylineCount;
        totalVisualElements.Should().BeGreaterThan(0, 
            "because chart should contain visual data elements (paths, circles, or polylines)");
    }

    [Test]
    [Order(6)]
    public async Task Chart_LineSeries_ShouldBeVisible()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Look for line series elements (path or polyline with stroke)
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Check for path elements with stroke (line visualization)
        var lineElements = Page.Locator(".rz-chart svg path[stroke], .rz-chart svg polyline[stroke]");
        var lineCount = await lineElements.CountAsync();

        // Assert
        lineCount.Should().BeGreaterThan(0, "because chart should have line series visualization");

        // Verify at least one line element has visible stroke
        if (lineCount > 0)
        {
            var firstLine = lineElements.First;
            var strokeColor = await firstLine.GetAttributeAsync("stroke");
            strokeColor.Should().NotBeNullOrEmpty("because line should have a stroke color");
            strokeColor.Should().NotBe("none", "because line stroke should be visible");
        }
    }

    [Test]
    [Order(7)]
    public async Task Chart_DataPoints_ShouldBeVisible()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Look for circle elements (data point markers)
        var circles = Page.Locator(".rz-chart svg circle");
        var circleCount = await circles.CountAsync();

        // Assert - Should have at least some data points
        // Note: Some charts may not show markers, so we'll make this a soft check
        if (circleCount > 0)
        {
            var firstCircle = circles.First;
            
            // Verify circle has fill or stroke
            var fill = await firstCircle.GetAttributeAsync("fill");
            var stroke = await firstCircle.GetAttributeAsync("stroke");
            
            (fill != null && fill != "none" || stroke != null && stroke != "none")
                .Should().BeTrue("because data point markers should be visible");
        }
        else
        {
            // If no circles, verify there are other visual elements
            var pathCount = await Page.Locator(".rz-chart svg path").CountAsync();
            pathCount.Should().BeGreaterThan(0, 
                "because if no circle markers, chart should have path elements");
        }
    }

    [Test]
    [Order(8)]
    public async Task Chart_Text_ShouldBeReadable()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Get text elements (axis labels, legends, etc.)
        var textElements = Page.Locator(".rz-chart svg text");
        var textCount = await textElements.CountAsync();

        // Assert - Should have text elements for labels
        textCount.Should().BeGreaterThan(0, "because chart should have axis labels or legends");

        if (textCount > 0)
        {
            var firstText = textElements.First;
            var textContent = await firstText.TextContentAsync();
            textContent.Should().NotBeNullOrWhiteSpace("because text labels should have content");

            // Verify text is not black on black (readable)
            var fill = await firstText.EvaluateAsync<string>(
                "element => window.getComputedStyle(element).fill"
            );
            fill.Should().NotBeNullOrEmpty("because text should have a fill color");
        }
    }

    [Test]
    [Order(9)]
    public async Task Chart_ShouldShowCommitHistory()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Wait for chart data to load
        await Task.Delay(3000);

        // Get the chart container
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Look for any visual indication of data
        var svgElement = Page.Locator(".rz-chart svg").First;
        var svgContent = await svgElement.InnerHTMLAsync();

        // Assert - SVG should contain data visualization elements
        svgContent.Should().NotBeNullOrEmpty();
        
        // Check for common chart elements
        var hasPath = svgContent.Contains("<path");
        var hasCircle = svgContent.Contains("<circle");
        var hasPolyline = svgContent.Contains("<polyline");
        var hasLine = svgContent.Contains("<line");
        
        (hasPath || hasCircle || hasPolyline || hasLine)
            .Should().BeTrue("because chart should render commit history with visual elements");
    }

    [Test]
    [Order(10)]
    public async Task RepositoryCard_ShouldShowStatistics()
    {
        // Arrange
        await NavigateToRepositoryChart();

        // Act - Look for repository statistics
        var repoCard = Page.Locator("div.card").First;
        await Expect(repoCard).ToBeVisibleAsync(new() { Timeout = 10000 });

        var cardContent = await repoCard.TextContentAsync();

        // Assert - Should show basic stats
        cardContent.Should().Contain(RepositoryName);
        
        // Should show commit count or line count
        (cardContent!.Contains("Commits") || 
         cardContent.Contains("Lines") ||
         cardContent.Contains("Files"))
            .Should().BeTrue("because repository card should display statistics");
    }

    /// <summary>
    /// Helper method to navigate to the repository and show the chart
    /// </summary>
    private async Task NavigateToRepositoryChart()
    {
        // Navigate to repositories page
        await Page.GotoAsync(RepositoriesPage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Click on repository link
        var repoLink = Page.Locator($"a:has-text('{RepositoryName}')");
        
        // Retry if needed (sometimes Blazor takes time to render)
        var retries = 0;
        while (retries < 3 && !await repoLink.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            await Page.ReloadAsync();
            await Task.Delay(2000);
            retries++;
        }
        
        await Expect(repoLink).ToBeVisibleAsync(new() { Timeout = 10000 });
        await repoLink.ClickAsync();

        // Wait for navigation
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Ensure chart is visible (click Show Chart if needed)
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')");
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')");
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(2000);
            }
        }

        // Wait for chart to render
        await Task.Delay(3000);
    }
}
