# Cleanup script to remove all Azure resources
# This deletes the entire resource group and all contained resources

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ResourceGroupName = "PoRepoLineTracker"

Write-Host "⚠️  WARNING: This will delete the resource group '$ResourceGroupName' and ALL resources within it!" -ForegroundColor Red

if (-not $Force) {
    $confirmation = Read-Host "Are you sure you want to continue? (yes/no)"
    if ($confirmation -ne "yes") {
        Write-Host "Cleanup cancelled." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "`nChecking if resource group exists..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroupName

if ($rgExists -eq "true") {
    Write-Host "Deleting resource group: $ResourceGroupName" -ForegroundColor Yellow
    az group delete --name $ResourceGroupName --yes --no-wait

    Write-Host "`n✅ Deletion initiated (running in background)" -ForegroundColor Green
    Write-Host "To check status: az group show --name $ResourceGroupName" -ForegroundColor Cyan
} else {
    Write-Host "Resource group '$ResourceGroupName' does not exist." -ForegroundColor Yellow
}
