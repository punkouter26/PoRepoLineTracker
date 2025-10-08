# Azure Setup Guide for PoRepoLineTracker

## Quick Start

### Local Development (Azurite)

1. **Install Azurite**
   ```powershell
   npm install -g azurite
   ```

2. **Start Azurite**
   ```powershell
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

3. **Run Application**
   ```powershell
   dotnet run --project src/PoRepoLineTracker.Api
   ```

   Application will be available at: http://localhost:5000

### Azure Deployment

1. **Login to Azure**
   ```powershell
   az login
   ```

2. **Deploy Infrastructure**
   ```powershell
   .\infra\deploy.ps1
   ```

   This creates:
   - Resource Group: `PoRepoLineTracker`
   - Storage Account: `porepolinetracker` (with 3 tables)
   - Log Analytics Workspace: `PoRepoLineTracker`
   - Application Insights: `PoRepoLineTracker`
   - App Service Plan: `PoRepoLineTracker` (Free tier)
   - App Service: `PoRepoLineTracker`

3. **Deploy Application**
   ```powershell
   azd up
   ```

## Resources Created

All resources use **free or cheapest tiers** and are created in the `PoRepoLineTracker` resource group:

| Resource | Name | SKU/Tier | Purpose |
|----------|------|----------|---------|
| Storage Account | porepolinetracker | Standard_LRS | Azure Table Storage for data |
| Log Analytics | PoRepoLineTracker | PerGB2018 | Telemetry storage |
| App Insights | PoRepoLineTracker | Pay-as-you-go | Application monitoring |
| App Service Plan | PoRepoLineTracker | F1 (Free) | Compute for web app |
| App Service | PoRepoLineTracker | - | Hosts Blazor + API |

### Azure Tables Created

1. **PoRepoLineTrackerRepositories** - Repository metadata
2. **PoRepoLineTrackerCommitLineCounts** - Commit line count data
3. **PoRepoLineTrackerFailedOperations** - Dead letter queue

## Configuration

### Local (appsettings.Development.json)

```json
{
  "GitHub": {
    "LocalReposPath": "C:\\LocalRepos",
    "PAT": ""
  },
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "RepositoryTableName": "PoRepoLineTrackerRepositories",
    "CommitLineCountTableName": "PoRepoLineTrackerCommitLineCounts",
    "FailedOperationTableName": "PoRepoLineTrackerFailedOperations"
  }
}
```

### Azure (Configured Automatically via Bicep)

Connection strings and table names are automatically set as App Service configuration during deployment.

## Testing

### Run Integration Tests

```powershell
# Ensure Azurite is running
azurite --silent --location c:\azurite --debug c:\azurite\debug.log

# Run all tests
dotnet test

# Run only Azure connectivity tests
dotnet test --filter FullyQualifiedName~AzureResourceConnectivityTests
```

## Cleanup

### Remove All Azure Resources

```powershell
# With confirmation
.\infra\cleanup.ps1

# Skip confirmation (use with caution)
.\infra\cleanup.ps1 -Force
```

Or using Azure CLI:

```powershell
az group delete --name PoRepoLineTracker --yes
```

## Cost Estimate

Expected monthly cost: **~$0** (within free tiers)

- Storage Account: First 5GB free, then $0.0184/GB
- App Service: F1 tier is free (1GB RAM, 60 min/day compute)
- Application Insights: First 5GB/month free
- Log Analytics: First 5GB/month free

## Troubleshooting

### Azurite Not Starting

```powershell
# Check if already running
Get-Process azurite

# Kill existing process
Stop-Process -Name azurite

# Start fresh
azurite --silent --location c:\azurite --debug c:\azurite\debug.log
```

### Deployment Fails

```powershell
# Check you're logged in
az account show

# Verify subscription
az account list --output table

# Set correct subscription
az account set --subscription "Your Subscription Name"
```

### Storage Account Name Already Exists

Storage account names are globally unique. If `porepolinetracker` is taken:

1. Edit `infra/main.bicep`
2. Change: `var storageAccountName = 'porepolinetracker<unique-suffix>'`
3. Redeploy

## File Structure

```
PoRepoLineTracker/
├── infra/
│   ├── main.bicep              # Main infrastructure template
│   ├── resources.bicep         # Resource definitions
│   ├── deploy.ps1              # Deployment script
│   ├── cleanup.ps1             # Cleanup script
│   └── README.md               # Detailed deployment docs
├── azure.yaml                  # Azure Developer CLI config
├── appsettings.json            # Base config (no secrets)
└── appsettings.Development.json # Local dev config
```

## Additional Resources

- **Deployment Details**: See `infra/README.md`
- **Project Documentation**: See `CLAUDE.md`
- **Integration Tests**: See `tests/PoRepoLineTracker.IntegrationTests/AzureResourceConnectivityTests.cs`
