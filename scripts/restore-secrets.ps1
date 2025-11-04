# Restore User Secrets Script
# Restores .NET User Secrets from backup

param(
    [string]$BackupPath = "$PSScriptRoot\..\secrets-backup.json"
)

$UserSecretsId = "a1b2c3d4-e5f6-7890-1234-567890abcdef"
$SecretsDir = "$env:APPDATA\Microsoft\UserSecrets\$UserSecretsId"
$SecretsPath = "$SecretsDir\secrets.json"

Write-Host "🔐 Restoring User Secrets..." -ForegroundColor Cyan

if (-not (Test-Path $BackupPath)) {
    Write-Host "❌ Backup file not found: $BackupPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please provide the backup file location:" -ForegroundColor Yellow
    Write-Host "  .\scripts\restore-secrets.ps1 -BackupPath 'C:\path\to\secrets-backup.json'" -ForegroundColor Yellow
    exit 1
}

# Create directory if it doesn't exist
if (-not (Test-Path $SecretsDir)) {
    New-Item -ItemType Directory -Path $SecretsDir -Force | Out-Null
    Write-Host "✅ Created secrets directory: $SecretsDir" -ForegroundColor Green
}

# Copy backup to secrets location
Copy-Item $BackupPath -Destination $SecretsPath -Force
Write-Host "✅ Secrets restored to: $SecretsPath" -ForegroundColor Green

# Verify
Write-Host ""
Write-Host "📋 Verifying secrets..." -ForegroundColor Cyan
Set-Location $PSScriptRoot\..
dotnet user-secrets list --project src/PoRepoLineTracker.Api

Write-Host ""
Write-Host "✅ Secrets restored successfully!" -ForegroundColor Green
