# PoRepoLineTracker — Agent Context

## Project Overview

PoRepoLineTracker is a Blazor WASM + ASP.NET Core application that tracks lines of code across GitHub repositories. It provides AI code detection, contributor statistics, and line trend analysis. Users authenticate via GitHub OAuth, Microsoft OAuth, or a GUEST mode for local development.

## Architecture

**Onion Architecture** with strict layer separation:

```
PoRepoLineTracker.Domain          → Entities, domain models (no dependencies)
PoRepoLineTracker.Application     → Business logic, interfaces, MediatR handlers (depends on Domain + Shared)
PoRepoLineTracker.Infrastructure  → Data access, external services (depends on Domain + Application)
PoRepoLineTracker.Shared          → DTOs shared between WASM client and API
PoRepoLineTracker.Api             → ASP.NET Core API + Blazor WASM host (depends on all above)
PoRepoLineTracker.Client          → Blazor WASM UI (depends on Shared + Domain only)
```

**Key Rule**: The Client project must NOT reference Application or Infrastructure directly. It communicates with the API via HTTP calls.

## Naming Convention

- Master prefix: `PoRepoLineTracker` for all namespaces, Azure resources, and configuration keys
- Key Vault secrets use `PoRepoLineTracker--` prefix (e.g., `PoRepoLineTracker--GitHub--ClientId`)
- Table names use `PoRepoLineTracker` prefix (e.g., `PoRepoLineTrackerRepositories`)

## Authentication

- **GitHub OAuth**: Primary auth via AspNet.Security.OAuth.GitHub
- **Microsoft OAuth**: Secondary auth via generic OAuth2 to Microsoft identity platform
- **GUEST Mode**: Dev-only. Clicking "Login as GUEST" creates a session with username `GUEST{random 8 digits}`. Persisted in LocalStorage. Button hidden in production.

## Configuration Hierarchy

1. `appsettings.json` — base config
2. `appsettings.Development.json` — local overrides (not committed with secrets)
3. `appsettings.Development.local.json` — local secrets (optional, gitignored)
4. Azure Key Vault — production secrets via `DefaultAzureCredential`
5. App Service Application Settings — fallback for critical keys

## Local Development

```powershell
# Start Azurite (Table Storage emulator)
docker compose up -d

# Run the API (hosts Blazor WASM on ports 5000/5001)
dotnet run --project src/PoRepoLineTracker.Api

# Run tests
dotnet test tests/PoRepoLineTracker.UnitTests
dotnet test tests/PoRepoLineTracker.IntegrationTests

# E2E tests
cd tests/PoRepoLineTracker.E2ETests.TS
npm install
npx playwright test
```

## Key Patterns

- **GoF Facade**: `ApiEndpointExtensions` unifies all endpoint registration
- **GoF Template Method**: `PrefixKeyVaultSecretManager` customizes secret loading
- **GoF Strategy**: Mock data banner uses runtime feature flag switching
- **SOLID — DIP**: All services registered by application-layer interfaces
- **SOLID — OCP**: Auth providers are additive without modifying existing ones
- **SOLID — ISP**: Each endpoint group has its own mapping method (VSA)
- **Composition Root**: `InfrastructureServiceExtensions` centralizes all DI
- **Vertical Slice Architecture**: Each feature area has its own endpoint file

## Azure Deployment

- Resource group: `PoShared` (shared services) + `PoRepoLineTracker` (app resources)
- Key Vault: `kv-poshared` in PoShared RG
- Storage: `stporepolinetracker` in PoRepoLineTracker RG
- App Service Plan: `asp-poshared-linux` in PoShared RG
- App Insights: shared instance in PoShared RG

## Coding Rules

- C# 14 features with `LangVersion=preview`
- `TreatWarningsAsErrors=true` globally
- `Nullable=enable` globally
- AOT disabled (`IsAotCompatible=false`)
- All patterns documented with XML `<summary>` tags explaining WHY
- Serilog with UserId, SessionId, CorrelationId enrichment
- OpenTelemetry with ActivitySource and Meter
