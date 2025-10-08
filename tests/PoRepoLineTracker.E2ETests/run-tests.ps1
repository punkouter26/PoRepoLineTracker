# Run E2E Tests for PoRepoLineTracker
# This script starts the application and runs the Playwright E2E tests

param(
    [string]$TestFilter = "",
    [switch]$Headed = $false,
    [switch]$Debug = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PoRepoLineTracker E2E Test Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Playwright is installed
Write-Host "Checking Playwright installation..." -ForegroundColor Yellow
try {
    $playwrightInstalled = Get-Command playwright -ErrorAction SilentlyContinue
    if (-not $playwrightInstalled) {
        Write-Host "Installing Playwright CLI..." -ForegroundColor Yellow
        dotnet tool install --global Microsoft.Playwright.CLI
    }
    
    Write-Host "Installing Playwright browsers (Chromium)..." -ForegroundColor Yellow
    playwright install chromium --with-deps
} catch {
    Write-Warning "Could not install Playwright browsers. Tests may fail."
}

# Start the API in background
Write-Host ""
Write-Host "Starting API server..." -ForegroundColor Yellow
$apiPath = Join-Path $PSScriptRoot "..\..\src\PoRepoLineTracker.Api"
Push-Location $apiPath

$apiJob = Start-Job -ScriptBlock {
    param($apiPath)
    Set-Location $apiPath
    dotnet run
} -ArgumentList $apiPath

Pop-Location

# Wait for API to start
Write-Host "Waiting for API to start on http://localhost:5000..." -ForegroundColor Yellow
$maxAttempts = 30
$attempt = 0
$apiReady = $false

while ($attempt -lt $maxAttempts -and -not $apiReady) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/healthz" -Method Get -TimeoutSec 2 -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            $apiReady = $true
            Write-Host "API is ready!" -ForegroundColor Green
        }
    } catch {
        $attempt++
        Start-Sleep -Seconds 1
    }
}

if (-not $apiReady) {
    Write-Host "API failed to start within 30 seconds. Stopping..." -ForegroundColor Red
    Stop-Job $apiJob
    Remove-Job $apiJob
    exit 1
}

# Set environment variables for test execution
if ($Headed) {
    $env:HEADED = "1"
}

if ($Debug) {
    $env:PWDEBUG = "1"
}

# Build test command
$testPath = Join-Path $PSScriptRoot "PoRepoLineTracker.E2ETests.csproj"
$testCommand = "dotnet test `"$testPath`""

if ($TestFilter) {
    $testCommand += " --filter `"$TestFilter`""
}

if ($Verbose) {
    $testCommand += " --logger `"console;verbosity=detailed`""
}

# Run the tests
Write-Host ""
Write-Host "Running E2E tests..." -ForegroundColor Yellow
Write-Host "Command: $testCommand" -ForegroundColor Gray
Write-Host ""

try {
    Invoke-Expression $testCommand
    $testExitCode = $LASTEXITCODE
} catch {
    Write-Host "Test execution failed: $_" -ForegroundColor Red
    $testExitCode = 1
}

# Cleanup
Write-Host ""
Write-Host "Stopping API server..." -ForegroundColor Yellow
Stop-Job $apiJob
Remove-Job $apiJob

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($testExitCode -eq 0) {
    Write-Host "✅ Tests Passed!" -ForegroundColor Green
} else {
    Write-Host "❌ Tests Failed!" -ForegroundColor Red
}
Write-Host "========================================" -ForegroundColor Cyan

exit $testExitCode
