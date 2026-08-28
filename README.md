# PoRepoLineTracker

PoRepoLineTracker is a self-hosted GitHub repository analytics app built with Blazor WebAssembly and an ASP.NET Core API. It authenticates with GitHub, tracks user-owned repositories, clones and analyzes commit history, persists derived metrics in Azure Table Storage, and surfaces line-count trends, extension breakdowns, and contributor statistics.

## Architecture overview

- Edge delivery: a Blazor WebAssembly client is served from the same App Service as the API.
- Compute tier: minimal APIs handle auth, settings, repository CRUD, GitHub lookups, and diagnostics; MediatR handlers and background tasks coordinate analysis, with live progress pushed over SignalR.
- Data tier: Azure Table Storage holds users, repositories, commit aggregates, and user preferences.
- External dependencies: GitHub provides OAuth identity, repository metadata, and clone/pull access; Azure Key Vault provides secrets; Application Insights collects telemetry (Jaeger via OTLP locally).

## Documentation

| Document | Purpose |
| --- | --- |
| [CLAUDE.md](CLAUDE.md) | Working notes: build/run/test, the traps worth knowing, domain rules |
| [NET_RULES.md](NET_RULES.md) | Numbered house rules (1.1–3.4) |
| [tests/README.md](tests/README.md) | Test scopes and local execution commands |

## Runtime summary

- Auth: GitHub OAuth (the only provider) issues an application cookie; user metadata and tokens are upserted to storage on sign-in.
- Repository management: repositories are added in bulk via `POST /api/repositories/bulk` — the single-add path was removed because it did not dedupe — then analyzed in the background.
- Analysis pipeline: the app clones or pulls repositories locally (no working-tree checkout), filters files by user-selected extensions and the vendored-code ignore rules, computes commit-level totals, and writes derived records back to Azure Table Storage.
- Presentation: the client reads repository history, extension percentages, contributor stats, and user preferences from the same API host.
- Operations: Serilog writes console and file logs in development; Application Insights receives cloud telemetry.

## Local development

### Prerequisites

- .NET 10 SDK
- Docker Desktop (Azurite + Jaeger via compose)

### Start dependencies

```powershell
docker compose up -d
```

### Run the app

```powershell
dotnet run --project src/PoRepoLineTracker.API --launch-profile https
```

### Authenticating without GitHub (Development/Test only)

There is no dev-login route. Tools and tests authenticate by sending an `X-Fake-User`
header (a GUID verbatim; any other string hashes to a stable GUID) — see
[src/PoRepoLineTracker.API/api-tests.http](src/PoRepoLineTracker.API/api-tests.http) for
worked examples including the antiforgery pair required by writes.

## Azure deployment

Deployment runs from GitHub Actions ([.github/workflows/deploy.yml](.github/workflows/deploy.yml)) on push to `master`: it lints and builds, deploys the app package to a Linux App Service, and can apply the Bicep in [infra/main.bicep](infra/main.bicep) / [infra/resources.bicep](infra/resources.bicep) via the manual `deploy_infra` input. The deployed topology is the combined API + WASM host backed by Azure Table Storage, Key Vault, and Application Insights.
