# How to View Azure App Service Logs

## Quick Reference

Your app is now configured with **Verbose logging** enabled. Here are all the ways to view logs:

---

## Option 1: Stream Live Logs (RECOMMENDED for Debugging)

### Method A: Using Azure CLI (Command Line)

Open PowerShell and run:

```powershell
az webapp log tail --name PoRepoLineTracker --resource-group PoRepoLineTracker
```

**What it does:**
- Shows live logs as they happen
- Perfect for watching logs while you test the app
- Press `Ctrl+C` to stop

**How to use:**
1. Open PowerShell terminal
2. Run the command above
3. Keep it running
4. In your browser, go to the app and try adding a repository
5. Watch the logs appear in real-time!

---

### Method B: Using Azure Portal (Web Interface)

1. Go to: https://portal.azure.com
2. Search for "PoRepoLineTracker" in the top search bar
3. Click on your App Service
4. In the left menu, under **Monitoring**, click **Log stream**
5. You'll see live logs streaming in the browser

---

## Option 2: View Application Insights Logs

### Method A: Azure Portal - Application Insights

1. Go to: https://portal.azure.com
2. Search for "PoRepoLineTracker" (the Application Insights resource)
3. Click on **Logs** in the left menu
4. Run queries like:

```kql
// See all logs from the last 30 minutes
traces
| where timestamp > ago(30m)
| order by timestamp desc

// Search for specific text
traces
| where timestamp > ago(1h)
| where message contains "AddMultipleRepositories"
| order by timestamp desc

// See errors only
traces
| where timestamp > ago(1h)
| where severityLevel >= 3
| order by timestamp desc
```

---

## Option 3: Download Log Files

### Download from Azure CLI

```powershell
# Download the latest log file
az webapp log download --name PoRepoLineTracker --resource-group PoRepoLineTracker --log-file logs.zip
```

Then extract `logs.zip` and look in the log files.

---

## Option 4: View in VS Code (If you have Azure Extension)

1. Install "Azure App Service" extension in VS Code
2. Sign in to Azure
3. In the Azure panel, expand your subscription
4. Expand "PoRepoLineTracker" App Service
5. Right-click → **Start Streaming Logs**

---

## What to Look For in the Logs

When you try to add a repository, you should see logs like this:

### Client-Side (Browser Console - Press F12):
```
=== START AddSelectedRepositories ===
Number of selected repositories: 1
Selected Repo [0]: FullName='punkouter26/PoRepoLineTracker', CloneUrl='...'
Prepared 1 repositories for API call
Sending Repo [0]: Owner='punkouter26', RepoName='PoRepoLineTracker', CloneUrl='...'
Making POST request to /api/repositories/bulk with 1 repositories
Bulk repository add response: Status=200
API returned 1 repositories
Returned Repo [0]: punkouter26/PoRepoLineTracker (ID: ...)
```

### Server-Side (Azure Logs):
```
=== BULK REPOSITORY ADD ENDPOINT CALLED ===
Request body received: NOT NULL
Number of repositories in request: 1
API Request Repo [0]: Owner='punkouter26', RepoName='PoRepoLineTracker', CloneUrl='...'
=== START AddMultipleRepositoriesCommandHandler ===
Received request to add 1 repositories
Processing repository: punkouter26/PoRepoLineTracker
Creating new repository entity for punkouter26/PoRepoLineTracker
Successfully saved repository punkouter26/PoRepoLineTracker
=== COMPLETED AddMultipleRepositoriesCommandHandler ===
Final result: Successfully added 1 out of 1 repositories
```

---

## Step-by-Step Debugging Process

### 1. Start Streaming Logs
```powershell
az webapp log tail --name PoRepoLineTracker --resource-group PoRepoLineTracker
```

### 2. Open Browser Console
- Go to: https://porepolinetracker.azurewebsites.net/add-repository
- Press **F12** to open Developer Tools
- Click on **Console** tab

### 3. Test Adding a Repository
- Click "Select from GitHub"
- Click the checkbox next to ONE repository
- The button should change to "Add Selected (1)"
- Click "Add Selected (1)"

### 4. Watch Both Logs Simultaneously
- **Browser Console**: Shows what the client is sending
- **PowerShell Terminal**: Shows what the server is receiving and processing

### 5. Compare the Logs
If you see:
- ✅ Browser shows "Sending Repo [0]: Owner='xxx', RepoName='yyy'"
- ❌ Server shows "Number of repositories in request: 0"
- **Problem**: Data not reaching the server (serialization issue)

If you see:
- ✅ Server shows "Received request to add 1 repositories"
- ❌ Server shows "Skipping repository with empty Owner"
- **Problem**: Data is null/empty (check the transformation logic)

---

## Useful Commands

### Check if logging is enabled:
```powershell
az webapp log config --name PoRepoLineTracker --resource-group PoRepoLineTracker --query "applicationLogs.fileSystem.level"
```

### Enable more detailed HTTP logs:
```powershell
az webapp log config --name PoRepoLineTracker --resource-group PoRepoLineTracker --web-server-logging filesystem
```

### View recent errors from Application Insights:
```powershell
az monitor app-insights query --app PoRepoLineTracker --resource-group PoRepoLineTracker --analytics-query "traces | where severityLevel >= 3 | where timestamp > ago(1h) | order by timestamp desc | take 20"
```

### Restart the app (if needed):
```powershell
az webapp restart --name PoRepoLineTracker --resource-group PoRepoLineTracker
```

---

## Troubleshooting

### "No logs appearing"
1. Make sure logging is enabled (see commands above)
2. Restart the app service
3. Wait 30 seconds and try again

### "Connection timeout"
- The log stream might disconnect after a while
- Just run the command again

### "Too many logs"
Filter the logs:
```powershell
# In PowerShell, pipe to Select-String
az webapp log tail --name PoRepoLineTracker --resource-group PoRepoLineTracker | Select-String "AddMultiple"
```

---

## Quick Test Script

Copy and paste this into PowerShell to set everything up and start watching logs:

```powershell
# Enable verbose logging
Write-Host "Enabling verbose logging..." -ForegroundColor Green
az webapp log config --name PoRepoLineTracker --resource-group PoRepoLineTracker --application-logging filesystem --level verbose

# Wait a moment
Start-Sleep -Seconds 2

# Start streaming logs
Write-Host "`nStarting log stream... (Press Ctrl+C to stop)" -ForegroundColor Yellow
Write-Host "Now go to: https://porepolinetracker.azurewebsites.net/add-repository" -ForegroundColor Cyan
Write-Host "And try adding a repository. Watch the logs below:`n" -ForegroundColor Cyan

az webapp log tail --name PoRepoLineTracker --resource-group PoRepoLineTracker
```

---

## Current Configuration

✅ **Application Logging**: Enabled (Filesystem, Verbose level)
✅ **Application Insights**: Connected
✅ **Log Analytics**: Available for querying

Your app is fully instrumented and ready for debugging! 🔍
