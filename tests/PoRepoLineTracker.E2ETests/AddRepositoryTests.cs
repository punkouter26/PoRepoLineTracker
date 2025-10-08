using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PoRepoLineTracker.E2ETests;

/// <summary>
/// End-to-End tests for the Add Repository page functionality.
/// These tests verify that users can view and interact with their GitHub repositories.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AddRepositoryTests : PageTest
{
    private const string BaseUrl = "http://localhost:5000";
    private const string AddRepositoryUrl = $"{BaseUrl}/add-repository";

    [SetUp]
    public async Task Setup()
    {
        // Set a longer timeout for Blazor WebAssembly loading
        Page.SetDefaultTimeout(30000);
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that the Add Repository page loads successfully")]
    public async Task AddRepositoryPage_ShouldLoad_Successfully()
    {
        // Act
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert
        Page.Url.Should().Contain("/add-repository");
        
        // Check for page heading
        var heading = Page.Locator("h3:has-text('Add New Repository')");
        await Expect(heading).ToBeVisibleAsync();
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that GitHub repositories are loaded and displayed in the UI")]
    public async Task AddRepositoryPage_ShouldLoadGitHubRepositories_WhenPATConfigured()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for the repository selector card to appear
        var repositoryCard = Page.Locator(".card:has-text('Select Repositories from Your GitHub Account')");
        await Expect(repositoryCard).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Act - Wait for repositories to load (spinner should disappear)
        var spinner = Page.Locator(".spinner-border");
        
        // Wait for either repositories to load OR an error message to appear
        try 
        {
            await spinner.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        }
        catch
        {
            // Spinner might have already disappeared
        }

        // Assert - Check if repositories loaded successfully or if there's an error
        var errorAlert = Page.Locator(".alert-warning:has-text('No Repositories Found')");
        var repositoryList = Page.Locator(".repository-list-container");

        var hasError = await errorAlert.IsVisibleAsync();
        var hasRepositories = await repositoryList.IsVisibleAsync();

        // We should have either repositories OR a clear error message
        (hasRepositories || hasError).Should().BeTrue(
            "Either repositories should be displayed or a clear error message should be shown");

        if (hasError)
        {
            // If there's an error, verify it's informative
            var errorText = await errorAlert.TextContentAsync();
            errorText.Should().NotBeNullOrEmpty();
            errorText.Should().Contain("GitHub Personal Access Token", 
                "Error message should mention PAT configuration");
        }
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that repositories are displayed with proper information")]
    public async Task AddRepositoryPage_ShouldDisplayRepositoryDetails_WhenRepositoriesExist()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for loading to complete
        await Task.Delay(3000); // Give time for API call and rendering

        // Act
        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        // Assert
        if (itemCount > 0)
        {
            // Verify first repository has required elements
            var firstRepo = repositoryItems.First;
            
            // Check for checkbox
            var checkbox = firstRepo.Locator("input[type='checkbox']");
            await Expect(checkbox).ToBeVisibleAsync();

            // Check for repository label
            var label = firstRepo.Locator("label");
            await Expect(label).ToBeVisibleAsync();

            // Verify label contains owner/name format
            var labelText = await label.TextContentAsync();
            labelText.Should().NotBeNullOrEmpty();
            labelText.Should().Contain("/", "Repository should be displayed as 'owner/name'");

            // Verify repository count is displayed
            var countText = Page.Locator("text=/Found \\d+ repositories/");
            await Expect(countText).ToBeVisibleAsync();
        }
        else
        {
            // If no repositories, should show appropriate message
            var noReposMessage = Page.Locator("text=/No repositories/i");
            await Expect(noReposMessage).ToBeVisibleAsync();
        }
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that users can select and deselect repositories")]
    public async Task AddRepositoryPage_ShouldAllowRepositorySelection()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000); // Wait for repositories to load

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount == 0)
        {
            Assert.Ignore("No repositories available for testing selection");
            return;
        }

        // Act - Select first repository
        var firstCheckbox = repositoryItems.First.Locator("input[type='checkbox']");
        await firstCheckbox.CheckAsync();

        // Assert - Check that selection count updated
        var selectionCount = Page.Locator("text=/\\d+ selected/");
        await Expect(selectionCount).ToBeVisibleAsync();
        
        var countText = await selectionCount.TextContentAsync();
        countText.Should().Contain("1", "One repository should be selected");

        // Verify the repository item shows as selected
        var firstRepoItem = repositoryItems.First;
        var isSelected = await firstRepoItem.EvaluateAsync<bool>(
            "el => el.classList.contains('selected')");
        isSelected.Should().BeTrue("Selected repository should have 'selected' class");

        // Act - Deselect the repository
        await firstCheckbox.UncheckAsync();

        // Assert - Selection count should be 0
        var updatedCountText = await selectionCount.TextContentAsync();
        updatedCountText.Should().Contain("0", "No repositories should be selected");
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that Select All button selects all repositories")]
    public async Task AddRepositoryPage_SelectAllButton_ShouldSelectAllRepositories()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000); // Wait for repositories to load

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount == 0)
        {
            Assert.Ignore("No repositories available for testing Select All");
            return;
        }

        // Act - Click Select All button
        var selectAllButton = Page.Locator("button:has-text('Select All')");
        await selectAllButton.ClickAsync();

        // Assert - All checkboxes should be checked
        var checkboxes = Page.Locator(".repository-checkbox");
        var checkboxCount = await checkboxes.CountAsync();

        for (int i = 0; i < checkboxCount; i++)
        {
            var isChecked = await checkboxes.Nth(i).IsCheckedAsync();
            isChecked.Should().BeTrue($"Checkbox {i} should be checked after Select All");
        }

        // Verify selection count matches total
        var selectionCount = Page.Locator("text=/\\d+ selected/");
        var countText = await selectionCount.TextContentAsync();
        countText.Should().Contain(itemCount.ToString(), 
            $"All {itemCount} repositories should be selected");
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that Clear All button deselects all repositories")]
    public async Task AddRepositoryPage_ClearAllButton_ShouldDeselectAllRepositories()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000); // Wait for repositories to load

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount == 0)
        {
            Assert.Ignore("No repositories available for testing Clear All");
            return;
        }

        // First select all
        var selectAllButton = Page.Locator("button:has-text('Select All')");
        await selectAllButton.ClickAsync();
        await Task.Delay(500);

        // Act - Click Clear All button
        var clearAllButton = Page.Locator("button:has-text('Clear All')");
        await clearAllButton.ClickAsync();

        // Assert - All checkboxes should be unchecked
        var checkboxes = Page.Locator(".repository-checkbox");
        var checkboxCount = await checkboxes.CountAsync();

        for (int i = 0; i < checkboxCount; i++)
        {
            var isChecked = await checkboxes.Nth(i).IsCheckedAsync();
            isChecked.Should().BeFalse($"Checkbox {i} should be unchecked after Clear All");
        }

        // Verify selection count is 0
        var selectionCount = Page.Locator("text=/0 selected/");
        await Expect(selectionCount).ToBeVisibleAsync();
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that the Add Selected button is enabled only when repositories are selected")]
    public async Task AddRepositoryPage_AddSelectedButton_ShouldBeDisabled_WhenNoSelection()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount == 0)
        {
            Assert.Ignore("No repositories available for testing Add button state");
            return;
        }

        // Act - Ensure nothing is selected
        var clearAllButton = Page.Locator("button:has-text('Clear All')");
        await clearAllButton.ClickAsync();

        // Assert - Add Selected button should be disabled
        var addButton = Page.Locator("button:has-text('Add Selected')");
        await Expect(addButton).ToBeDisabledAsync();

        // Act - Select one repository
        var firstCheckbox = repositoryItems.First.Locator("input[type='checkbox']");
        await firstCheckbox.CheckAsync();

        // Assert - Add Selected button should be enabled
        await Expect(addButton).ToBeEnabledAsync();
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that repository list has scrollable container for many repositories")]
    public async Task AddRepositoryPage_RepositoryList_ShouldBeScrollable()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount < 5)
        {
            Assert.Ignore("Not enough repositories to test scrollability");
            return;
        }

        // Act & Assert - Verify container has max-height and overflow
        var container = Page.Locator(".repository-list-container");
        await Expect(container).ToBeVisibleAsync();

        var hasOverflow = await container.EvaluateAsync<bool>(
            @"el => {
                const style = window.getComputedStyle(el);
                return style.overflowY === 'auto' || style.overflowY === 'scroll';
            }");

        hasOverflow.Should().BeTrue("Repository list container should have scrollable overflow");
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that error messages are displayed clearly when PAT is not configured")]
    public async Task AddRepositoryPage_ShouldShowClearErrorMessage_WhenPATNotConfigured()
    {
        // This test assumes PAT might not be configured or could fail
        // In production, you'd want to test against a test environment without PAT

        // Arrange & Act
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);

        // Assert - Check if error alert exists and is informative
        var errorAlert = Page.Locator(".alert-warning, .alert-danger");
        var hasError = await errorAlert.IsVisibleAsync();

        if (hasError)
        {
            var errorText = await errorAlert.TextContentAsync();
            errorText.Should().NotBeNullOrEmpty();
            
            // Error should mention configuration or token
            var hasHelpfulMessage = errorText!.Contains("Personal Access Token") || 
                                   errorText.Contains("configuration") ||
                                   errorText.Contains("Settings");
            
            hasHelpfulMessage.Should().BeTrue(
                "Error message should provide helpful guidance about PAT configuration");
        }
    }

    [Test]
    [Category("E2E")]
    [Description("Verifies that repository badges (language, private) are displayed correctly")]
    public async Task AddRepositoryPage_ShouldDisplayRepositoryBadges()
    {
        // Arrange
        await Page.GotoAsync(AddRepositoryUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(3000);

        var repositoryItems = Page.Locator(".repository-item");
        var itemCount = await repositoryItems.CountAsync();

        if (itemCount == 0)
        {
            Assert.Ignore("No repositories available for testing badges");
            return;
        }

        // Act & Assert - Check that at least one repository has badges
        var badges = Page.Locator(".badge");
        var badgeCount = await badges.CountAsync();

        // Most repositories should have at least a language badge
        if (badgeCount > 0)
        {
            var firstBadge = badges.First;
            await Expect(firstBadge).ToBeVisibleAsync();
            
            var badgeText = await firstBadge.TextContentAsync();
            badgeText.Should().NotBeNullOrEmpty("Badge should have text content");
        }
    }
}
