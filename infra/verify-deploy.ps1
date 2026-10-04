# verify-deploy.ps1 — Smoke-test the live Azure deployment from this machine.
#
# The same checks as the post-deploy probe in .github/workflows/deploy.yml, against
# https://app-porepolinetracker.azurewebsites.net: /health answers 200, the sign-in page loads,
# a protected API route refuses an anonymous caller with 401, and an unknown API route is a 404.
# Each path has ONE acceptable status; anything else fails. The /health call is what Azure's own
# probe uses; the others prove the static shell and the auth layer are wired correctly — a
# /health that answers 200 from a half-deployed package would still 500 on the rest.
#
# Usage: .\infra\verify-deploy.ps1
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

# /api/repositories and /api/diagnostics: 401 when anonymous — the API auth layer rejects, it
# does not redirect, so callers can tell "you need to log in" from "this URL is wrong". This
# list used to accept 200 for /api/diagnostics, directly under a comment saying a 200 there
# means the auth layer is broken.
$paths = @(
    @{ Path = '/health';                        Accept = @(200) },
    @{ Path = '/login';                         Accept = @(200) },
    @{ Path = '/api/repositories';              Accept = @(401) },
    @{ Path = '/api/diagnostics';               Accept = @(401) },
    @{ Path = '/api/no-such-route-smoke-test';  Accept = @(404) }
)

$failed = $false
foreach ($entry in $paths) {
    $full = "$Url$($entry.Path)"
    try {
        $response = Invoke-WebRequest -Uri $full -UseBasicParsing -MaximumRetry 3 -RetryIntervalSec 2 -SkipHttpErrorCheck -MaximumRedirection 0 -TimeoutSec 20
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
