# verify-deploy.ps1 — Smoke-test the live Azure deployment from this machine.
#
# Mirrors the post-deploy probe in .github/workflows/deploy.yml: hits /health, /, and /diag
# against https://app-porepolinetracker.azurewebsites.net and treats any 4xx/5xx as a failure.
# The /health call is what Azure's own probe uses, so a green here means a green on the platform
# probe as well. The / and /diag calls prove the static shell and the auth-redirect layer are
# wired correctly — a /health that answers 200 from a half-deployed package would still 500 on /.
#
# Usage: .\SCRIPTS\verify-deploy.ps1
#
# No parameters: the target URL is hard-coded so a developer cannot accidentally point this at
# staging or a colleague's fork. Override the env var $env:PoRepoLineTracker_VerifyUrl if you
# genuinely need to (e.g. testing a custom domain).

$ErrorActionPreference = 'Stop'

$Url = if ($env:PoRepoLineTracker_VerifyUrl) {
    $env:PoRepoLineTracker_VerifyUrl.TrimEnd('/')
} else {
    'https://app-porepolinetracker.azurewebsites.net'
}

# /health: anonymous, used by Azure's probe AND by us. /: 302 to /login when anonymous.
# /diag: 302 to /login when anonymous. Both 200 and 302 are acceptable "alive" responses.
$paths = @(
    @{ Path = '/health'; Accept = @(200) },
    @{ Path = '/';      Accept = @(200, 302) },
    @{ Path = '/diag';  Accept = @(200, 302) }
)

$failed = $false
foreach ($entry in $paths) {
    $full = "$Url$($entry.Path)"
    try {
        $response = Invoke-WebRequest -Uri $full -UseBasicParsing -MaximumRetry 3 -RetryIntervalSec 2 -SkipHttpErrorCheck -TimeoutSec 20
        $code = [int]$response.StatusCode
    } catch {
        Write-Host "::error::$($entry.Path) threw: $_"
        $failed = $true
        continue
    }

    $marker = if ($entry.Accept -contains $code) { 'OK ' } else { 'BAD' }
    if ($marker -eq 'BAD') { $failed = $true }
    Write-Host ("{0} {1,-6} {2}" -f $marker, $code, $full)
}

if ($failed) {
    Write-Host "`nverify-deploy.ps1: one or more probes failed." -ForegroundColor Red
    exit 1
}

Write-Host "`nverify-deploy.ps1: deployment is alive at $Url" -ForegroundColor Green
