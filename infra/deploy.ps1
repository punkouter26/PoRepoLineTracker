# Deploy Azure infrastructure using Azure CLI and Bicep
# This script creates all required Azure resources for PoRepoLineTracker

param(
    [string]$Location = "eastus",
    [string]$SubscriptionId = ""
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Deploying PoRepoLineTracker Azure Infrastructure" -ForegroundColor Green

# Set subscription if provided
if ($SubscriptionId) {
    Write-Host "Setting subscription to: $SubscriptionId" -ForegroundColor Yellow
    az account set --subscription $SubscriptionId
}

# Get current subscription
$currentSub = az account show --query name -o tsv
Write-Host "Using subscription: $currentSub" -ForegroundColor Cyan

# Deploy at subscription scope
Write-Host "`nDeploying resources..." -ForegroundColor Yellow
az deployment sub create `
    --name "PoRepoLineTracker-deployment-$(Get-Date -Format 'yyyyMMddHHmmss')" `
    --location $Location `
    --template-file ".\infra\main.bicep" `
    --parameters location=$Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Deployment completed successfully!" -ForegroundColor Green

    # Get outputs
    Write-Host "`nRetrieving deployment outputs..." -ForegroundColor Yellow
    $outputs = az deployment sub show `
        --name "PoRepoLineTracker-deployment-$(Get-Date -Format 'yyyyMMddHHmmss')" `
        --query properties.outputs -o json | ConvertFrom-Json

    Write-Host "`n📋 Deployment Information:" -ForegroundColor Cyan
    Write-Host "Resource Group: PoRepoLineTracker"
    Write-Host "Location: $Location"
    Write-Host "`nTo view resources: az resource list --resource-group PoRepoLineTracker --output table"
} else {
    Write-Host "`n❌ Deployment failed!" -ForegroundColor Red
    exit 1
}
