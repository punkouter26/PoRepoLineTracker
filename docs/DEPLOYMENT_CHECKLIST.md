# Deployment Checklist - Commit Filtering Fix

## Pre-Deployment
- [x] All unit tests pass (8/8 tests)
- [x] Solution builds without errors
- [x] Changes documented in `docs/COMMIT_FILTERING_FIX.md`
- [x] HTTP test file created for manual verification

## Code Changes Summary
1. **GitHubService.cs**: Fixed `GetCommitStatsAsync` to properly filter commits by date using UTC
2. **GitClient.cs**: Fixed `GetCommits` to use UTC date comparison
3. **AnalyzeRepositoryCommitsCommandHandler.cs**: Changed to use `DateTime.UtcNow` instead of `DateTime.Now`
4. **GitHubServiceCommitFilteringTests.cs**: Added unit tests for date filtering logic

## Deployment Steps

### 1. Build and Test Locally
```powershell
# Build the solution
dotnet build

# Run all unit tests
dotnet test

# Run the application locally
dotnet run --project src/PoRepoLineTracker.Api
```

### 2. Deploy to Azure
```powershell
# Using Azure CLI
az webapp deployment source config-zip --resource-group <resource-group> --name porepolinetracker --src <zip-file-path>

# OR using Azure Developer CLI
azd deploy
```

### 3. Post-Deployment Verification

#### Check Application Logs in Azure Portal
1. Navigate to App Service → Log Stream
2. Look for these log messages:
   - "Filtering commits since {SinceDate} (UTC)"
   - "Processing {CommitCount} commits after date filtering"
   - "Found {CommitCount} commits to analyze for repository {RepositoryId}"

#### Verify Functionality
Use the `tests/api-commit-filtering-tests.http` file to:

1. **Add a repository**
   ```http
   POST https://porepolinetracker.azurewebsites.net/api/repositories
   ```

2. **Analyze the repository**
   ```http
   POST https://porepolinetracker.azurewebsites.net/api/repositories/{id}/analyze
   ```

3. **Check line count history** (should NOT show "No line count history returned")
   ```http
   GET https://porepolinetracker.azurewebsites.net/api/repositories/{id}/history?days=365
   ```

4. **Check file extension percentages** (should NOT show errors)
   ```http
   GET https://porepolinetracker.azurewebsites.net/api/repositories/{id}/file-extensions
   ```

#### Expected Results
- ✅ No "No line count history returned" warnings
- ✅ No "Error fetching file extension percentages" errors
- ✅ No "Error fetching line count history" errors
- ✅ Chart data displays correctly in the UI
- ✅ File extension percentages calculated and displayed

## Rollback Plan
If issues occur after deployment:

1. Check Application Insights for errors
2. Review deployment logs
3. If needed, rollback to previous version:
   ```powershell
   az webapp deployment slot swap --resource-group <resource-group> --name porepolinetracker --slot staging --target-slot production
   ```

## Monitoring After Deployment

### Key Metrics to Watch
1. **Application Insights**:
   - Custom trace logs for commit filtering
   - Exception count (should remain low)
   - Response times for `/analyze` endpoint

2. **Azure Table Storage**:
   - Check that `CommitLineCounts` table is being populated
   - Verify data is being written with correct dates

3. **UI Behavior**:
   - Charts should load without "Chart Data Unavailable" messages
   - Repository analysis should complete successfully

## Known Issues & Mitigations

### Issue: Ephemeral Storage in Azure App Service
- **Impact**: Cloned repositories may be deleted on app restart
- **Mitigation**: The code handles this by re-cloning if the local path is invalid

### Issue: Large Repositories
- **Impact**: Analysis may timeout for very large repositories
- **Mitigation**: The 365-day filter significantly reduces commits to process

## Support & Troubleshooting

If you encounter issues:

1. **Check logs**: Azure Portal → App Service → Log Stream
2. **Application Insights**: Look for custom traces and exceptions
3. **Test locally**: Use Azurite to replicate the issue locally
4. **Review documentation**: `docs/COMMIT_FILTERING_FIX.md`

## Success Criteria
- [x] All tests pass before deployment
- [ ] Application deploys successfully to Azure
- [ ] No errors in Application Insights logs
- [ ] Repository analysis completes successfully
- [ ] Line count history displays correctly
- [ ] File extension percentages display correctly
- [ ] No "No line count history" warnings in console
