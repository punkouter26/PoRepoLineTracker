# SCRIPTS

| Script | Purpose |
|--------|---------|
| `setup.ps1` | **First-run setup** for a new machine. Installs Docker, the .NET 10 SDK and the Azure CLI via Winget, starts the compose services, checks `az login` for Key Vault access, and frees ports 5002/5003 of orphaned dotnet processes. |
| `verify-deploy.ps1` | **Post-deploy smoke probe** against the live site — the same checks the pipeline runs, from your own network. Override the target with `$env:PoRepoLineTracker_VerifyUrl`. |

Run either from the repo root: `.\SCRIPTS\setup.ps1`, `.\SCRIPTS\verify-deploy.ps1`.

Each script's header comment carries its own detail — usage, why the URL is hard-coded, what each
probe proves. This file is the index, not a second copy of that; two descriptions of one script
drift, and the one further from the code is the one that goes stale.

Docker must be running: `docker compose up -d` starts Azurite (Table Storage emulator, ports
10000-10002) and Jaeger (traces, <http://localhost:16686>).
