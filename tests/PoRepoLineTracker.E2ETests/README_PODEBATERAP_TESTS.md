# PoDebateRap E2E Tests

## Overview
This test suite adds the **PoDebateRap** private repository and verifies that the chart visualization displays correctly with visual data.

## Repository Details
- **URL**: https://github.com/punkouter26/PoDebateRap
- **Type**: Private repository
- **Owner**: punkouter26
- **Purpose**: Test private repository access and chart visualization

## Prerequisites

### 1. GitHub Personal Access Token
Ensure your GitHub PAT is configured in `appsettings.Development.json`:

```json
{
  "GitHubPAT": "your-github-pat-here"
}
```

The PAT must have access to the `punkouter26/PoDebateRap` repository.

### 2. API Server Running
The API must be running on `http://localhost:5000` before running tests.

### 3. Playwright Browsers Installed
```powershell
pwsh tests/PoRepoLineTracker.E2ETests/bin/Debug/net9.0/playwright.ps1 install
```

## Running the Tests

### Option 1: Using PowerShell Script (Recommended)
```powershell
cd tests/PoRepoLineTracker.E2ETests
./run-podebaterap-tests.ps1
```

This script will:
- Check if API is running (start it if needed)
- Run all AddPoDebateRapTests
- Display detailed test results
- Clean up after completion

### Option 2: Using dotnet test
```powershell
# Start API first
cd src/PoRepoLineTracker.Api
dotnet run &

# In another terminal, run tests
cd tests/PoRepoLineTracker.E2ETests
dotnet test --filter "FullyQualifiedName~AddPoDebateRapTests" --logger "console;verbosity=detailed"
```

### Option 3: Run Single Test
```powershell
dotnet test --filter "FullyQualifiedName~AddPoDebateRapTests.Should_AddPoDebateRapRepository_Successfully"
```

## Test Suite

### Test Execution Order
Tests are ordered to ensure proper flow:

1. **Should_AddPoDebateRapRepository_Successfully** (Order 1)
   - Adds the repository via the UI
   - Waits for analysis to complete
   - Verifies ANALYZED status

2. **Should_NavigateToPoDebateRapRepository_AndShowChart** (Order 2)
   - Navigates to repository details
   - Verifies chart container is visible

3. **Chart_ShouldHaveWhiteBackground** (Order 3)
   - Validates chart has white/light background
   - Ensures chart is not displaying as black box

4. **Chart_ShouldContainSVGElement** (Order 4)
   - Verifies SVG element exists in chart
   - Checks SVG has valid dimensions

5. **Chart_ShouldContainVisualDataElements** (Order 5)
   - Looks for paths, circles, or polylines
   - Ensures visual elements exist

6. **Chart_LineSeries_ShouldBeVisible** (Order 6)
   - Verifies line series has stroke color
   - Validates line visualization is present

7. **Chart_DataPoints_ShouldBeVisible** (Order 7)
   - Checks for circle markers (data points)
   - Verifies markers have fill or stroke

8. **Chart_Text_ShouldBeReadable** (Order 8)
   - Validates axis labels and legends
   - Ensures text is not black-on-black

9. **Chart_ShouldShowCommitHistory** (Order 9)
   - Comprehensive check for data visualization
   - Validates chart renders commit history

10. **RepositoryCard_ShouldShowStatistics** (Order 10)
    - Verifies repository statistics display
    - Checks for commit/line/file counts

## What These Tests Verify

### Repository Addition Flow
- ✅ Form accepts GitHub repository URL
- ✅ Repository is added to the system
- ✅ Analysis completes successfully
- ✅ Repository appears in list with ANALYZED status

### Chart Visualization
- ✅ Chart container renders on page
- ✅ Chart has white/light background (not black)
- ✅ SVG element exists with valid dimensions
- ✅ Visual data elements present (paths/circles/polylines)
- ✅ Line series has visible stroke color
- ✅ Data point markers are visible (if present)
- ✅ Axis labels and text are readable
- ✅ Chart displays commit history data

### Private Repository Access
- ✅ GitHub PAT authentication works
- ✅ Private repository can be cloned
- ✅ Commit history is accessible
- ✅ Line counting analysis succeeds

## Expected Results

All 10 tests should **PASS** if:
- GitHub PAT is valid and has access to PoDebateRap
- API is running and accessible
- Chart CSS fixes are applied correctly
- Repository analysis completes successfully

## Troubleshooting

### Test: Should_AddPoDebateRapRepository_Successfully Fails
**Issue**: Repository not added or analysis fails

**Solutions**:
1. Check GitHub PAT is valid:
   ```powershell
   curl -H "Authorization: token YOUR_PAT" https://api.github.com/repos/punkouter26/PoDebateRap
   ```
2. Check API logs for errors
3. Verify Azurite is running for storage
4. Ensure repository URL is correct

### Test: Chart_ShouldHaveWhiteBackground Fails
**Issue**: Chart still showing black background

**Solutions**:
1. Clear browser cache
2. Rebuild client project:
   ```powershell
   cd src/PoRepoLineTracker.Client
   dotnet clean
   dotnet build
   ```
3. Verify app.css contains chart visibility fixes
4. Hard refresh browser (Ctrl+Shift+R)

### Test: Chart_ShouldContainVisualDataElements Fails
**Issue**: No visual elements in chart

**Solutions**:
1. Check if repository has commits:
   ```bash
   git log --oneline
   ```
2. Verify line count analysis completed
3. Check API endpoint returns data:
   ```powershell
   curl http://localhost:5000/api/repositories/{repo-id}/linehistory/365
   ```
4. Look for JavaScript errors in browser console

### Test: Chart_LineSeries_ShouldBeVisible Fails
**Issue**: Line series not rendering

**Solutions**:
1. Verify Radzen.Blazor version is compatible
2. Check CSS for polyline/path styling
3. Inspect SVG element in browser dev tools
4. Ensure chart data is not empty

## Test Configuration

### Timeouts
- Page load: 10 seconds
- Element visibility: 15 seconds
- Repository analysis: 60 seconds (with retries)
- Chart rendering: 3 seconds

### Retries
- Repository link visibility: 20 retries (3 seconds each)
- Page reload on element not found: 3 retries

### Delays
- Blazor render wait: 2 seconds
- Chart data load: 3 seconds
- After button click: 1-2 seconds

## Success Criteria

✅ **10/10 tests passing** = Complete success
- Repository added successfully
- Chart displays with white background
- Visual data elements present and visible
- Commit history rendered correctly

⚠️ **7-9/10 tests passing** = Partial success
- Core functionality works
- Minor CSS or timing issues
- Review failed tests for specifics

❌ **<7/10 tests passing** = Failure
- Authentication or analysis issues
- Chart rendering broken
- Review logs and troubleshoot

## Related Documentation
- [PrivateRepositoryAccessTests.cs](../../tests/PoRepoLineTracker.IntegrationTests/PrivateRepositoryAccessTests.cs) - Integration tests for private repo access
- [ChartVisibilityTests.cs](ChartVisibilityTests.cs) - General chart visibility tests
- [CHART_VISIBILITY_FIX.md](../../CHART_VISIBILITY_FIX.md) - Chart CSS fix documentation

## Notes
- Tests use **Playwright** for browser automation
- Tests run in **Chromium** browser by default
- Tests are **parallelizable** (ParallelScope.Self)
- Tests use **NUnit** framework with FluentAssertions
- Test execution order is enforced using `[Order]` attribute
