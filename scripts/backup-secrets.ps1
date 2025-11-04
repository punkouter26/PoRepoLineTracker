# Backup User Secrets Script
# Backs up .NET User Secrets to a safe location

param(
    [string]$BackupPath = "$PSScriptRoot\..\secrets-backup.json"
)

$UserSecretsId = "a1b2c3d4-e5f6-7890-1234-567890abcdef"
$SecretsPath = "$env:APPDATA\Microsoft\UserSecrets\$UserSecretsId\secrets.json"

Write-Host "🔐 Backing up User Secrets..." -ForegroundColor Cyan

if (Test-Path $SecretsPath) {
    Copy-Item $SecretsPath -Destination $BackupPath -Force
    Write-Host "✅ Secrets backed up to: $BackupPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "⚠️  IMPORTANT: Keep this file secure!" -ForegroundColor Yellow
    Write-Host "   - Do NOT commit to Git (already in .gitignore)" -ForegroundColor Yellow
    Write-Host "   - Store in password manager or encrypted location" -ForegroundColor Yellow
    Write-Host "   - Delete local backup after storing securely" -ForegroundColor Yellow
} else {
    Write-Host "❌ No secrets found at: $SecretsPath" -ForegroundColor Red
    Write-Host "   Run: dotnet user-secrets list --project src/PoRepoLineTracker.Api" -ForegroundColor Yellow
}
