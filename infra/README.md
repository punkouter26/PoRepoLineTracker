# Azure Infrastructure Deployment

This directory contains Infrastructure as Code (IaC) using Bicep templates to deploy PoRepoLineTracker to Azure.

## Resources Created

All resources are created in a single resource group named **PoRepoLineTracker** with the following components:

1. **Azure Storage Account** (`porepolinetracker`)
   - SKU: Standard_LRS (cheapest option)
   - Three Azure Tables:
     - `PoRepoLineTrackerRepositories`
     - `PoRepoLineTrackerCommitLineCounts`
     - `PoRepoLineTrackerFailedOperations`

2. **Log Analytics Workspace** (`PoRepoLineTracker`)
   - SKU: PerGB2018 (pay-as-you-go)
   - Retention: 30 days (minimum)

3. **Application Insights** (`PoRepoLineTracker`)
   - Type: Web
   - Linked to Log Analytics Workspace

4. **App Service Plan** (`PoRepoLineTracker`)
   - SKU: F1 (Free tier)
   - OS: Linux
   - Runtime: .NET 9.0

5. **App Service** (`PoRepoLineTracker`)
   - Linked to App Service Plan
   - Pre-configured with connection strings

## Prerequisites

- Azure CLI installed ([Install Guide](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli))
- Azure subscription
- Logged in to Azure CLI: `az login`

## Local Development with Azurite

For local development, the app uses **Azurite** (Azure Storage Emulator):

1. Install Azurite:
   ```powershell
   npm install -g azurite
   ```

2. Start Azurite:
   ```powershell
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

3. Configuration in `appsettings.Development.json`:
   ```json
   {
     "AzureTableStorage": {
       "ConnectionString": "UseDevelopmentStorage=true"
     }
   }
   ```

## Deployment

### Option 1: Using Azure Developer CLI (azd) - Recommended

```powershell
# Initialize (first time only)
azd init

# Provision infrastructure and deploy
azd up
```

### Option 2: Using PowerShell Script

```powershell
# Deploy to default location (eastus)
.\infra\deploy.ps1

# Deploy to specific location
.\infra\deploy.ps1 -Location "westus2"

# Deploy to specific subscription
.\infra\deploy.ps1 -SubscriptionId "your-subscription-id"
```

### Option 3: Using Azure CLI Directly

```powershell
# Deploy at subscription scope
az deployment sub create \
  --name "PoRepoLineTracker-deployment" \
  --location "eastus" \
  --template-file ".\infra\main.bicep" \
  --parameters location="eastus"
```

## Verify Deployment

After deployment, verify resources were created:

```powershell
# List all resources in the resource group
az resource list --resource-group PoRepoLineTracker --output table

# Check specific resources
az storage account show --name porepolinetracker --resource-group PoRepoLineTracker
az webapp show --name PoRepoLineTracker --resource-group PoRepoLineTracker
az monitor app-insights component show --app PoRepoLineTracker --resource-group PoRepoLineTracker
```

## Configuration

The Bicep templates automatically configure the App Service with these settings:

- Application Insights connection string
- Azure Table Storage connection string
- Table names for all three tables

No manual configuration is required after deployment.

## Testing

Run integration tests to verify Azure connectivity:

```powershell
# Make sure Azurite is running for local tests
azurite --silent --location c:\azurite --debug c:\azurite\debug.log

# Run integration tests
dotnet test tests/PoRepoLineTracker.IntegrationTests/PoRepoLineTracker.IntegrationTests.csproj
```

Specific Azure connectivity tests are in:
- `AzureResourceConnectivityTests.cs`

## Cleanup

To delete all Azure resources:

```powershell
# With confirmation prompt
.\infra\cleanup.ps1

# Without confirmation (use with caution)
.\infra\cleanup.ps1 -Force
```

Or using Azure CLI:

```powershell
az group delete --name PoRepoLineTracker --yes
```

## Cost Optimization

All resources use the cheapest/free tiers:

- **Storage Account**: Standard_LRS (locally-redundant storage)
- **App Service Plan**: F1 (Free tier, 1GB RAM, 60 min/day)
- **Application Insights**: Pay-as-you-go (free tier: 5GB/month)
- **Log Analytics**: Pay-as-you-go (free tier: 5GB/month)

Expected monthly cost: **~$0** (within free tiers)

## Architecture

```
┌─────────────────────────────────────────┐
│   App Service (PoRepoLineTracker)      │
│   - Blazor WebAssembly + API            │
└─────────────────┬───────────────────────┘
                  │
          ┌───────┴────────┐
          │                │
┌─────────▼──────┐  ┌──────▼─────────────┐
│ Storage Account │  │ Application        │
│ - Repositories  │  │ Insights           │
│ - CommitCounts  │  │                    │
│ - FailedOps     │  └────────┬───────────┘
└─────────────────┘           │
                    ┌─────────▼──────────┐
                    │ Log Analytics      │
                    │ Workspace          │
                    └────────────────────┘
```

## Troubleshooting

### Deployment Fails

1. Ensure you're logged in: `az login`
2. Check subscription: `az account show`
3. Verify permissions: You need Contributor role on the subscription

### Resource Names Already Exist

The deployment uses fixed names. If resources exist from a previous deployment:

```powershell
.\infra\cleanup.ps1 -Force
.\infra\deploy.ps1
```

### Storage Account Name Conflict

Storage account names must be globally unique. If `porepolinetracker` is taken, edit `infra/main.bicep`:

```bicep
var storageAccountName = 'porepolinetracker<your-unique-suffix>'
```

### Azurite Connection Issues

Make sure Azurite is running before starting the app locally:

```powershell
# Check if Azurite is running
Get-Process azurite

# If not, start it
azurite --silent --location c:\azurite --debug c:\azurite\debug.log
```
