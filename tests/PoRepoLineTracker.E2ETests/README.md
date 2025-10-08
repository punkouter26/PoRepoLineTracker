# E2E Tests for PoRepoLineTracker

## Overview
This test suite contains end-to-end (E2E) tests using Playwright to verify the functionality of the PoRepoLineTracker application's user interface.

## Test Files

### AddRepositoryTests.cs
Comprehensive E2E tests for the Add Repository page functionality, including:

- **Page Loading**: Verifies the page loads successfully
- **GitHub Repository Loading**: Tests that repositories are fetched and displayed from GitHub API
- **Repository Display**: Validates that repository information (name, owner, description, badges) is shown correctly
- **Selection Functionality**: Tests checkbox selection, Select All, and Clear All features
- **Add Button State**: Verifies the Add Selected button is enabled/disabled appropriately
- **Scrollable List**: Tests that the repository list has proper overflow handling
- **Error Handling**: Validates that clear error messages are shown when PAT is not configured
- **Badge Display**: Checks that language and private badges are rendered

### BasicFunctionalityTests.cs
Basic functional tests for overall application navigation and health checks.

## Prerequisites

### 1. Install Playwright Browsers
Before running the tests, you need to install Playwright browsers:

```powershell
# Install Playwright CLI globally
dotnet tool install --global Microsoft.Playwright.CLI

# Install browsers (run from the E2E test project directory)
cd tests\PoRepoLineTracker.E2ETests
playwright install
```

Or install just Chromium (recommended for CI/CD):
```powershell
playwright install chromium
```

### 2. Start the Application
The E2E tests expect the application to be running on `http://localhost:5000`:

```powershell
# From the root directory
cd src\PoRepoLineTracker.Api
dotnet run
```

**Important**: Make sure your `appsettings.Development.json` has the GitHub PAT configured:
```json
{
  "GitHub": {
    "PAT": "your_github_personal_access_token_here"
  }
}
```

You can get your token from GitHub CLI:
```powershell
gh auth token
```

## Running the Tests

### Run All E2E Tests
```powershell
# From the root directory
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj
```

### Run Only AddRepository Tests
```powershell
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj --filter "FullyQualifiedName~AddRepositoryTests"
```

### Run a Specific Test
```powershell
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj --filter "FullyQualifiedName~AddRepositoryPage_ShouldLoadGitHubRepositories_WhenPATConfigured"
```

### Run with Detailed Output
```powershell
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj --logger "console;verbosity=detailed"
```

### Run in Headed Mode (See Browser)
By default, Playwright runs in headless mode. To see the browser:

Modify the test or add an environment variable:
```powershell
$env:HEADED="1"
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj
```

Or add to your test file:
```csharp
[SetUp]
public async Task Setup()
{
    await using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = false });
}
```

## Test Categories

Tests are categorized for easier filtering:

```powershell
# Run only E2E category tests
dotnet test --filter "Category=E2E"
```

## Debugging Tests

### Enable Playwright Inspector
```powershell
$env:PWDEBUG="1"
dotnet test tests\PoRepoLineTracker.E2ETests\PoRepoLineTracker.E2ETests.csproj --filter "FullyQualifiedName~AddRepositoryPage_ShouldLoad_Successfully"
```

### Slow Down Test Execution
```csharp
BrowserTypeLaunchOptions options = new() { SlowMo = 1000 }; // 1 second delay
```

### Take Screenshots on Failure
The tests are configured to automatically capture screenshots and videos on failure.

## CI/CD Integration

For CI/CD pipelines, ensure:

1. Playwright browsers are installed as part of the build:
   ```yaml
   - name: Install Playwright
     run: |
       dotnet tool install --global Microsoft.Playwright.CLI
       playwright install --with-deps chromium
   ```

2. The application is started before tests:
   ```yaml
   - name: Start Application
     run: |
       cd src/PoRepoLineTracker.Api
       dotnet run &
       sleep 10  # Wait for app to start
   ```

3. Environment variables are set:
   ```yaml
   - name: Run E2E Tests
     env:
       GitHub__PAT: ${{ secrets.GITHUB_PAT }}
     run: dotnet test tests/PoRepoLineTracker.E2ETests
   ```

## Troubleshooting

### Tests Fail with "Browser Not Installed"
Run: `playwright install chromium`

### Tests Timeout
- Increase timeout in test: `Page.SetDefaultTimeout(60000)` (60 seconds)
- Ensure the application is running on localhost:5000
- Check that Blazor WebAssembly has fully loaded

### GitHub API Errors
- Verify your GitHub PAT is configured in `appsettings.Development.json`
- Check PAT has correct permissions (repo scope)
- Test the API endpoint directly: `http://localhost:5000/api/github/user-repositories`

### Tests Pass Locally but Fail in CI
- Ensure browsers are installed with `--with-deps` flag
- Check that the application URL is correct
- Verify environment variables are properly set
- Consider adding longer delays for slower CI environments

## Test Coverage

The AddRepositoryTests suite covers:
- ✅ Page navigation and loading
- ✅ API integration with GitHub
- ✅ UI component rendering
- ✅ User interactions (clicks, selections)
- ✅ Form validation
- ✅ Error handling and messaging
- ✅ Responsive design (scrollability)
- ✅ Accessibility features

## Future Enhancements

- [ ] Add tests for bulk repository addition flow
- [ ] Test progress bar during repository analysis
- [ ] Add visual regression testing
- [ ] Test with different screen sizes (mobile, tablet)
- [ ] Add performance testing (load time metrics)
- [ ] Test offline behavior
