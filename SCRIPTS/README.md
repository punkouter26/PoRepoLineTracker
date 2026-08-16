# SCRIPTS

Utility scripts for local development and maintenance of PoRepoLineTracker.

| Script | Purpose |
|--------|---------|
| `setup.ps1` | **First-run setup** for new machines. Installs prerequisites (Docker, .NET 10 SDK, Azure CLI) via Winget, starts the compose services (Azurite + Jaeger), checks `az login` for Key Vault access, and kills orphaned dotnet processes on ports 5000/5001. |

## First-Run Setup (New Machine)

```powershell
# From the repo root — installs everything needed for a fresh checkout
.\SCRIPTS\setup.ps1
```

## Prerequisites

- Docker must be running; `docker compose up -d` from the repo root starts Azurite
  (Table Storage emulator, ports 10000-10002) and Jaeger (trace UI on
  <http://localhost:16686>).
- PowerShell 5.1+ or PowerShell 7+.
