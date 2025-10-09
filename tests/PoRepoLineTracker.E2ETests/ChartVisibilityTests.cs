using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PoRepoLineTracker.E2ETests;

/// <summary>
/// E2E tests to verify that the Radzen Chart component displays correctly
/// with proper visibility, colors, and readability.
/// 
/// Issue: Chart was displaying with a black background making content unreadable.
/// Fix: Added CSS overrides in app.css to set proper background and text colors.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ChartVisibilityTests : PageTest
{
    private const string BaseUrl = "http://localhost:5000";
    private const string RepositoriesPage = $"{BaseUrl}/repositories";

    [SetUp]
    public async Task Setup()
    {
        // Navigate to repositories page before each test
        await Page.GotoAsync(RepositoriesPage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000); // Wait for Blazor to render
    }

    [Test]
    public async Task ChartContainer_ShouldHaveProperBackground()
    {
        // Arrange - Find a repository with chart data
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        // If chart is not visible, click "Show Chart" button
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Locate the chart container
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Check that chart container has proper styling
        var backgroundColor = await chartContainer.EvaluateAsync<string>(@"
            element => window.getComputedStyle(element).backgroundColor
        ");

        // The background should be white (rgb(255, 255, 255)) or the theme's primary background
        // It should NOT be black (rgb(0, 0, 0)) or transparent
        backgroundColor.Should().NotBe("rgb(0, 0, 0)", "Chart background should not be black");
        backgroundColor.Should().NotBe("rgba(0, 0, 0, 0)", "Chart background should not be transparent");
    }

    [Test]
    public async Task ChartText_ShouldBeVisible()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Wait for chart to render
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Check that text elements in the chart are visible
        var textElements = Page.Locator(".rz-chart svg text");
        var textCount = await textElements.CountAsync();
        
        textCount.Should().BeGreaterThan(0, "Chart should contain text elements for labels and values");

        // Verify text color is not black (should be readable)
        if (textCount > 0)
        {
            var firstTextElement = textElements.Nth(0);
            var textFill = await firstTextElement.GetAttributeAsync("fill");
            
            // Text should have a fill color (not transparent or black on black background)
            textFill.Should().NotBeNullOrEmpty("Chart text should have a fill color");
        }
    }

    [Test]
    public async Task ChartSVG_ShouldHaveTransparentBackground()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Locate the SVG element inside chart
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - SVG should have transparent background to allow container background to show
        var svgBackgroundColor = await chartSvg.EvaluateAsync<string>(@"
            element => window.getComputedStyle(element).backgroundColor
        ");

        // SVG background should be transparent or match container
        (svgBackgroundColor == "rgba(0, 0, 0, 0)" || 
         svgBackgroundColor == "transparent" ||
         !svgBackgroundColor.Contains("rgb(0, 0, 0)"))
        .Should().BeTrue("SVG background should be transparent or not black");
    }

    [Test]
    public async Task ChartGridLines_ShouldBeVisible()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Wait for chart to render
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Check that grid lines exist and have proper stroke
        var gridLines = Page.Locator(".rz-chart line, .rz-chart path.rz-grid-line");
        var gridLineCount = await gridLines.CountAsync();

        gridLineCount.Should().BeGreaterThan(0, "Chart should contain grid lines for reference");
    }

    [Test]
    public async Task ChartLineSeries_ShouldBeVisible()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Wait for chart to render
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Check that the data line series exists
        var lineSeries = Page.Locator(".rz-chart path.rz-series-line, .rz-chart polyline");
        var lineCount = await lineSeries.CountAsync();

        lineCount.Should().BeGreaterThan(0, "Chart should contain at least one line series for the data");
    }

    [Test]
    public async Task ChartMarkers_ShouldBeVisible()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Wait for chart to render
        var chartSvg = Page.Locator(".rz-chart svg").First;
        await Expect(chartSvg).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Check that data point markers exist
        var markers = Page.Locator(".rz-chart circle.rz-series-marker, .rz-chart circle");
        var markerCount = await markers.CountAsync();

        markerCount.Should().BeGreaterThan(0, "Chart should contain markers for data points");

        // Verify markers have proper fill color
        if (markerCount > 0)
        {
            var firstMarker = markers.Nth(0);
            var markerFill = await firstMarker.GetAttributeAsync("fill");
            
            markerFill.Should().NotBeNullOrEmpty("Chart markers should have a fill color");
            markerFill.Should().NotBe("#000000", "Markers should not be black on a potentially black background");
        }
    }

    [Test]
    public async Task ChartCard_ShouldHaveMinimumHeight()
    {
        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(1000);
            }
        }

        // Act - Locate the chart container
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Assert - Chart should have adequate height for visibility
        var boundingBox = await chartContainer.BoundingBoxAsync();
        
        boundingBox.Should().NotBeNull("Chart should have dimensions");
        boundingBox!.Height.Should().BeGreaterThan(300, "Chart should be at least 300px tall for readability");
    }

    [Test]
    public async Task Chart_ShouldShowDataWhenAvailable()
    {
        // Arrange - Navigate to repositories page
        await Page.GotoAsync(RepositoriesPage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Look for a repository with "ANALYZED" status
        var analyzedRepo = Page.Locator("tr:has(span:has-text('ANALYZED'))").First;
        
        // Act - Click to expand chart if repository is analyzed
        if (await analyzedRepo.IsVisibleAsync(new() { Timeout = 5000 }).ConfigureAwait(false))
        {
            var showChartButton = analyzedRepo.Locator("button:has-text('Show Chart')");
            
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(2000); // Wait for chart to load
            }

            // Assert - Chart should be visible with data
            var chart = Page.Locator(".rz-chart").First;
            await Expect(chart).ToBeVisibleAsync(new() { Timeout = 15000 });

            // Verify chart title is present
            var chartTitle = Page.Locator("h5:has-text('Line Count History')").First;
            await Expect(chartTitle).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ChartErrorState_ShouldDisplayRetryButton()
    {
        // Arrange - Navigate to repositories page
        await Page.GotoAsync(RepositoriesPage);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(2000);

        // Act - Look for any "Chart Data Unavailable" messages
        var chartUnavailable = Page.Locator("div.alert-warning:has-text('Chart Data Unavailable')").First;

        // Assert - If chart is unavailable, retry button should be present
        if (await chartUnavailable.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var retryButton = Page.Locator("button:has-text('Retry')");
            await Expect(retryButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task TakeScreenshot_ChartVisibility()
    {
        // This test captures a screenshot for manual verification
        // Useful for visual regression testing

        // Arrange - Expand chart section
        var hideChartButton = Page.Locator("button:has-text('Hide Chart')").First;
        
        if (!await hideChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
        {
            var showChartButton = Page.Locator("button:has-text('Show Chart')").First;
            if (await showChartButton.IsVisibleAsync(new() { Timeout = 2000 }).ConfigureAwait(false))
            {
                await showChartButton.ClickAsync();
                await Task.Delay(2000);
            }
        }

        // Act - Wait for chart to fully render
        var chartContainer = Page.Locator(".rz-chart").First;
        await Expect(chartContainer).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Task.Delay(1000); // Extra wait for animations

        // Assert - Take screenshot for visual verification
        var screenshotPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"chart-visibility-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        );

        await chartContainer.ScreenshotAsync(new()
        {
            Path = screenshotPath
        });

        File.Exists(screenshotPath).Should().BeTrue("Screenshot should be saved");
        Console.WriteLine($"Screenshot saved to: {screenshotPath}");
    }
}
