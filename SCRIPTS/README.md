# SCRIPTS

Utility scripts for local development and maintenance of PoRepoLineTracker.

| Script | Purpose |
|--------|---------|
| `setup.ps1` | **First-run setup** for new machines. Installs prerequisites (Docker, .NET 10 SDK, Azure CLI) via Winget, starts the compose services (Azurite + Jaeger), checks `az login` for Key Vault access, and kills orphaned dotnet processes on ports 5002/5003. |
| `verify-deploy.ps1` | **Post-deploy smoke probe** for the live Azure site. Hits `/health`, `/`, and `/diag` against `app-porepolinetracker.azurewebsites.net` from the developer's machine. Useful after triggering `workflow_dispatch`, or when the pipeline is green but you want to confirm from your own network. Override the target with `$env:PoRepoLineTracker_VerifyUrl`. |

## First-Run Setup (New Machine)

```powershell
# From the repo root — installs everything needed for a fresh checkout
.\SCRIPTS\setup.ps1
```

## Verifying a Deployment

```powershell
# From anywhere — defaults to https://app-porepolinetracker.azurewebsites.net
.\SCRIPTS\verify-deploy.ps1
```

## Prerequisites

- Docker must be running; `docker compose up -d` from the repo root starts Azurite
  (Table Storage emulator, ports 10000-10002) and Jaeger (trace UI on
  <http://localhost:16686>).
- PowerShell 5.1+ or PowerShell 7+.
