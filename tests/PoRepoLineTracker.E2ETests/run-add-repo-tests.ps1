# Quick test runner for AddRepository E2E tests
# Usage: .\run-add-repo-tests.ps1

$ErrorActionPreference = "Stop"

Write-Host "Running AddRepository E2E Tests..." -ForegroundColor Cyan
Write-Host ""

# Navigate to E2E test directory
$testDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $testDir

# Run the main test runner with filter for AddRepository tests
& .\run-tests.ps1 -TestFilter "FullyQualifiedName~AddRepositoryTests" -Verbose

Write-Host ""
Write-Host "See README.md for more test options and troubleshooting." -ForegroundColor Gray
