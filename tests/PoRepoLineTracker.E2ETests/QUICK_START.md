# Quick Test Execution Guide

## 🚀 Run Tests Now (3 Simple Steps)

### Step 1: Open PowerShell in Project Root
```powershell
cd C:\Users\punko\Downloads\PoRepoLineTracker
```

### Step 2: Install Playwright Browsers (One-Time Setup)
```powershell
cd tests\PoRepoLineTracker.E2ETests
pwsh -c "playwright install chromium"
```

### Step 3: Run the Tests
```powershell
.\run-add-repo-tests.ps1
```

That's it! The script will:
1. ✅ Start the API server automatically
2. ✅ Wait for it to be ready
3. ✅ Run all 12 AddRepository tests
4. ✅ Show you the results
5. ✅ Clean up and stop the server

---

## 🎬 Alternative: Manual Test Execution

### Terminal 1: Start API
```powershell
cd C:\Users\punko\Downloads\PoRepoLineTracker
cd src\PoRepoLineTracker.Api
dotnet run
```

Wait for this message:
```
[INFO] Now listening on: http://localhost:5000
```

### Terminal 2: Run Tests
```powershell
cd C:\Users\punko\Downloads\PoRepoLineTracker
cd tests\PoRepoLineTracker.E2ETests
dotnet test --filter "FullyQualifiedName~AddRepositoryTests" --logger "console;verbosity=normal"
```

---

## 🔍 Run Specific Tests

### Test Page Loading
```powershell
dotnet test --filter "AddRepositoryPage_ShouldLoad_Successfully"
```

### Test GitHub Integration
```powershell
dotnet test --filter "AddRepositoryPage_ShouldLoadGitHubRepositories_WhenPATConfigured"
```

### Test Selection Features
```powershell
dotnet test --filter "FullyQualifiedName~Selection"
```

---

## 👀 Watch Tests Run (Visible Browser)

```powershell
.\run-tests.ps1 -Headed -TestFilter "AddRepositoryPage_ShouldLoad_Successfully"
```

This will open a browser window and you can watch the test interact with your application!

---

## 📊 Expected Output

### Successful Test Run:
```
Test run for PoRepoLineTracker.E2ETests.dll (.NETCoreApp,Version=v9.0)
Microsoft (R) Test Execution Command Line Tool Version 17.12.0

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 45s
```

### With Individual Test Details:
```
✅ AddRepositoryPage_ShouldLoad_Successfully (2.1s)
✅ AddRepositoryPage_ShouldLoadGitHubRepositories_WhenPATConfigured (4.3s)
✅ AddRepositoryPage_ShouldDisplayRepositoryDetails_WhenRepositoriesExist (3.8s)
✅ AddRepositoryPage_ShouldAllowRepositorySelection (2.9s)
✅ AddRepositoryPage_SelectAllButton_ShouldSelectAllRepositories (3.2s)
✅ AddRepositoryPage_ClearAllButton_ShouldDeselectAllRepositories (2.7s)
✅ AddRepositoryPage_AddSelectedButton_ShouldBeDisabled_WhenNoSelection (2.5s)
✅ AddRepositoryPage_RepositoryList_ShouldBeScrollable (2.1s)
✅ AddRepositoryPage_ShouldShowClearErrorMessage_WhenPATNotConfigured (2.3s)
✅ AddRepositoryPage_ShouldDisplayRepositoryBadges (2.6s)
```

---

## ❌ If Tests Fail

### Check Prerequisites:
```powershell
# 1. Is API running?
curl http://localhost:5000/healthz

# 2. Is GitHub PAT configured?
Get-Content src\PoRepoLineTracker.Api\appsettings.Development.json

# 3. Are Playwright browsers installed?
playwright --version
```

### Common Fixes:
```powershell
# Reinstall browsers
playwright install chromium --with-deps

# Restart API
cd src\PoRepoLineTracker.Api
dotnet build
dotnet run

# Rebuild tests
cd tests\PoRepoLineTracker.E2ETests
dotnet build
```

---

## 🎯 What Tests Verify

After running these tests successfully, you can confirm:

✅ GitHub Personal Access Token is configured correctly  
✅ GitHub API integration is working  
✅ Repositories are fetched from your GitHub account  
✅ Repositories are displayed in the UI  
✅ All UI interactions work (selection, buttons)  
✅ Error handling is user-friendly  
✅ UI styling (overflow, scrolling) works correctly  

---

## 📹 Video Tutorial (Steps)

1. **Open PowerShell** → Navigate to project
2. **Run installer** → `playwright install chromium`
3. **Execute script** → `.\run-add-repo-tests.ps1`
4. **Watch results** → Green checkmarks = success!

---

## 💡 Pro Tips

### Run Faster (Parallel)
```powershell
dotnet test --parallel
```

### See More Details
```powershell
.\run-tests.ps1 -Verbose
```

### Debug Failed Test
```powershell
.\run-tests.ps1 -Debug -TestFilter "TestNameThatFailed"
```

### Run in VS Code
1. Open Test Explorer
2. Find AddRepositoryTests
3. Click "Run All Tests" icon
4. View results in Test Explorer

---

## ✨ First Time Running?

Copy and paste this entire block:

```powershell
# Navigate to project
cd C:\Users\punko\Downloads\PoRepoLineTracker\tests\PoRepoLineTracker.E2ETests

# One-time setup
pwsh -c "playwright install chromium"

# Run tests
.\run-add-repo-tests.ps1
```

---

**That's it!** You should now see 12 passing tests confirming that GitHub repositories are accessible in your UI! 🎉
