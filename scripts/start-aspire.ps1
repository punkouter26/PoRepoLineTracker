# Starts the Aspire AppHost in the background and redirects output to a log file.
# This avoids interactive prompts by setting variables explicitly and using --non-interactive.

$log = 'C:\Users\punko\aspire-dashboard.log'
if (Test-Path -Path $log) {
    Remove-Item -Path $log -Force
}

# Build the command that will be executed by the child PowerShell process
$aspireCommand = "aspire run --project src/PoRepoLineTracker.AppHost --non-interactive *>&1 | Out-File -FilePath '$log' -Encoding utf8 -Append"

# Start the child PowerShell process detached so the caller doesn't block
Start-Process -FilePath pwsh -ArgumentList '-NoProfile','-Command',$aspireCommand -WindowStyle Hidden -PassThru | Out-Null

Start-Sleep -Seconds 2
Write-Output "Aspire start requested. Output will be written to: $log"

# Show the last lines of the log if it exists (useful when executing interactively)
if (Test-Path -Path $log) {
    Get-Content -Path $log -Tail 50
} else {
    Write-Output 'Log file not yet created; the process is starting in background.'
}