# SCRIPTS

Utility scripts for local development and maintenance of PoRepoLineTracker.

| Script | Purpose |
|--------|---------|
| `setup.ps1` | **First-run setup** for new machines. Installs prerequisites (Docker, .NET 10 SDK, Azure CLI) via Winget, starts Azurite, checks `az login` for Key Vault access, and kills orphaned dotnet processes on ports 5000/5001. |
| `patch-azurite.ps1` | Directly MERGE-patches a single Azure Table Storage entity in the local Azurite emulator using SharedKeyLite authentication. Useful for manually fixing corrupt/stuck records during development without needing the full API stack. Update the `$partitionKey`, `$rowKey`, and `$localPath` variables at the top before running. |

## First-Run Setup (New Machine)

```powershell
# From the repo root — installs everything needed for a fresh checkout
.\SCRIPTS\setup.ps1
```

## Prerequisites

- Docker must be running with the Azurite container started (`docker compose up -d` from the repo root).
- PowerShell 5.1+ or PowerShell 7+.

## Usage

```powershell
# Start Azurite first
docker compose up -d

# Then run the patch script (edit variables at top of script first)
.\SCRIPTS\patch-azurite.ps1
```
