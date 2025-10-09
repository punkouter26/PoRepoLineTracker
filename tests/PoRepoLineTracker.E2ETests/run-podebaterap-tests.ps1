# Run E2E Tests for PoDebateRap Repository Addition and Chart Verification
# This script runs the API and executes E2E tests for the AddPoDebateRapTests test suite

$ErrorActionPreference = "Stop"

Write-Host "=== PoDebateRap E2E Test Runner ===" -ForegroundColor Cyan
Write-Host ""

# Check if API is already running
$apiRunning = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | 
    Where-Object { $_.MainModule.FileName -like "*dotnet.exe*" }

if (-not $apiRunning) {
    Write-Host "Starting API server..." -ForegroundColor Yellow
    
    # Start API in background
    $apiPath = Join-Path $PSScriptRoot "..\..\src\PoRepoLineTracker.Api"
    $apiJob = Start-Job -ScriptBlock {
        param($path)
        Set-Location $path
        dotnet run
    } -ArgumentList $apiPath
    
    Write-Host "Waiting for API to start (30 seconds)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30
    
    # Test if API is responding
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/api/repositories" -TimeoutSec 10 -UseBasicParsing
        Write-Host "API is responding (Status: $($response.StatusCode))" -ForegroundColor Green
    }
    catch {
        Write-Host "Warning: Could not verify API is running. Continuing anyway..." -ForegroundColor Yellow
    }
} else {
    Write-Host "API is already running" -ForegroundColor Green
}

Write-Host ""
Write-Host "Running AddPoDebateRapTests E2E tests..." -ForegroundColor Cyan
Write-Host "This will:" -ForegroundColor White
Write-Host "  1. Add the PoDebateRap repository (if not already added)" -ForegroundColor White
Write-Host "  2. Verify the repository is analyzed successfully" -ForegroundColor White
Write-Host "  3. Navigate to the repository details page" -ForegroundColor White
Write-Host "  4. Verify the chart displays with proper visibility" -ForegroundColor White
Write-Host "  5. Verify chart contains visual data elements" -ForegroundColor White
Write-Host ""

# We're already in the test project directory
# Navigate to test project directory (already there via $PSScriptRoot)
Set-Location $PSScriptRoot

# Run the specific test class
Write-Host "Executing tests..." -ForegroundColor Cyan
dotnet test --filter "FullyQualifiedName~AddPoDebateRapTests" --logger "console;verbosity=detailed"

$testExitCode = $LASTEXITCODE

Write-Host ""
if ($testExitCode -eq 0) {
    Write-Host "=== All tests PASSED ===" -ForegroundColor Green
} else {
    Write-Host "=== Some tests FAILED ===" -ForegroundColor Red
    Write-Host "Exit code: $testExitCode" -ForegroundColor Red
}

# Cleanup: Stop API if we started it
if ($apiJob) {
    Write-Host ""
    Write-Host "Stopping API server..." -ForegroundColor Yellow
    Stop-Job -Job $apiJob
    Remove-Job -Job $apiJob
}

Write-Host ""
Write-Host "Test run complete!" -ForegroundColor Cyan

exit $testExitCode
