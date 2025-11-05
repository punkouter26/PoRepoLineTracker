# Commit Filtering Fix - Azure Deployment Issue

## Problem
When running in Azure, the application was not picking up all commit changes from repositories. The console showed warnings about "No line count history returned for repository" and errors fetching file extension percentages and line count history.

## Root Cause
The issue was caused by improper date filtering when fetching commits from Git repositories:

1. **Missing Date Filter in `GetCommitStatsAsync`**: The method received a `sinceDate` parameter but never applied it to the `CommitFilter`, causing it to fetch ALL commits regardless of the date range.

2. **DateTime vs DateTimeOffset Comparison**: The code was comparing `DateTime` values with `DateTimeOffset.DateTime`, which could cause timezone-related issues when filtering commits.

3. **Local vs UTC Time**: The code used `DateTime.Now` instead of `DateTime.UtcNow`, which could cause inconsistencies in Azure (which operates in UTC).

## Changes Made

### 1. Fixed `GitHubService.GetCommitStatsAsync` (GitHubService.cs)
**Before:**
```csharp
var filter = new CommitFilter
{
    SortBy = CommitSortStrategies.Time
};

var commits = repo.Commits.QueryBy(filter).ToList();
```

**After:**
```csharp
var filter = new CommitFilter
{
    SortBy = CommitSortStrategies.Time,
    IncludeReachableFrom = repo.Head
};

var commits = repo.Commits.QueryBy(filter);

// Apply date filter if provided
if (sinceDate.HasValue)
{
    _logger.LogInformation("Filtering commits since {SinceDate} (UTC)", sinceDate.Value);
    // Convert sinceDate to UTC for comparison with commit dates
    var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
    commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
}

var commitsList = commits.ToList();
_logger.LogInformation("Processing {CommitCount} commits after date filtering", commitsList.Count);
```

### 2. Fixed `GitClient.GetCommits` (GitClient.cs)
**Before:**
```csharp
IEnumerable<Commit> commits = repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time });

if (sinceDate.HasValue)
{
    commits = commits.Where(c => c.Author.When >= sinceDate.Value);
}
```

**After:**
```csharp
IEnumerable<Commit> commits = repo.Commits.QueryBy(new CommitFilter 
{ 
    SortBy = CommitSortStrategies.Time,
    IncludeReachableFrom = repo.Head
});

if (sinceDate.HasValue)
{
    // Convert sinceDate to UTC for consistent comparison
    var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
    commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
}
```

### 3. Updated `AnalyzeRepositoryCommitsCommandHandler` (AnalyzeRepositoryCommitsCommandHandler.cs)
**Before:**
```csharp
var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, DateTime.Now.AddDays(-365));
```

**After:**
```csharp
var sinceDate = DateTime.UtcNow.AddDays(-365);
_logger.LogInformation("Fetching commit stats since {SinceDate} for repository {RepositoryId}", sinceDate, request.RepositoryId);
var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, sinceDate);
```

### 4. Added Unit Tests (GitHubServiceCommitFilteringTests.cs)
Created comprehensive unit tests to verify:
- UTC date conversion logic
- Date comparison logic for commit filtering
- Edge cases (commits on cutoff date, before cutoff, after cutoff)

All tests pass successfully.

## Benefits

1. **Correct Date Filtering**: Commits are now properly filtered by date, improving performance and reducing unnecessary processing
2. **UTC Consistency**: All date comparisons use UTC time, eliminating timezone-related bugs in Azure
3. **Better Logging**: Added detailed logging to track the filtering process
4. **Test Coverage**: Added unit tests to prevent regression

## Testing Recommendations

When deploying to Azure:
1. Check the Application Insights logs for the new log messages showing commit count after filtering
2. Verify that repositories show line count history after analysis
3. Confirm that file extension percentages are calculated correctly
4. Monitor for any "No line count history" warnings - they should be resolved

## Related Files
- `src/PoRepoLineTracker.Infrastructure/Services/GitHubService.cs`
- `src/PoRepoLineTracker.Infrastructure/Services/GitClient.cs`
- `src/PoRepoLineTracker.Application/Features/Repositories/Commands/AnalyzeRepositoryCommitsCommandHandler.cs`
- `tests/PoRepoLineTracker.UnitTests/GitHubServiceCommitFilteringTests.cs`
