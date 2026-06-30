# PoRepoLineTracker — Agent Context

## Project Overview

PoRepoLineTracker is a Blazor WASM + ASP.NET Core application that tracks lines of code across GitHub repositories. It provides AI code detection, contributor statistics, and line trend analysis. Users authenticate via GitHub OAuth, Microsoft OAuth, or a GUEST mode for local development.

## Architecture

**Onion Architecture** with strict layer separation:

```
PoRepoLineTracker.Shared          → DTOs + FluentValidation rules + shared contract enums (LEAF — no project refs)
PoRepoLineTracker.Domain          → Entities, domain models (depends on Shared for contract enums)
PoRepoLineTracker.Application     → Business logic, interfaces, MediatR handlers (depends on Domain + Shared)
PoRepoLineTracker.Infrastructure  → Data access, external services (depends on Domain + Application)
PoRepoLineTracker.Api             → ASP.NET Core API + Blazor WASM host (depends on all above)
PoRepoLineTracker.Client          → Blazor WASM UI (depends on Shared + Domain only)
```

**Key Rule**: The Client project must NOT reference Application or Infrastructure directly. It communicates with the API via HTTP calls.

**Shared is the leaf (Rule 2.2)**: `.Shared` holds DTOs and FluentValidation rules only and has **no project references**. The shared-contract enums (`AlertMetric`, `AlertOperator`, `ChartDisplayMode`) physically live in `.Shared` (`Models/SharedContractEnums.cs`) but keep the `PoRepoLineTracker.Domain.Models` namespace so existing `using` directives resolve unchanged; `.Domain` references `.Shared` to consume them. DTOs never embed Domain entities — wire shapes like `GitHubRepositoryDto` mirror them instead.

## Naming Convention

- Master prefix: `PoRepoLineTracker` for all namespaces, Azure resources, and configuration keys
- Key Vault secrets use `PoRepoLineTracker--` prefix (e.g., `PoRepoLineTracker--GitHub--ClientId`)
- Table names use `PoRepoLineTracker` prefix (e.g., `PoRepoLineTrackerRepositories`)

## Authentication

- **Routes (Rule 4.4)**: `/auth/login` (generic, prefers GitHub), `/auth/login/microsoft`, `/auth/login/fake` (guest bypass), `/auth/logout`, `/auth/me`. (Renamed from the former `/api/auth/*` shape.)
- **GitHub OAuth**: Primary auth via AspNet.Security.OAuth.GitHub
- **Microsoft OAuth**: Secondary auth via generic OAuth2 to Microsoft identity platform `/common`. Tenant allow-list (Rule 4.3) via `Microsoft:AllowedTenants` (comma-separated tenant IDs); the `tid` claim is validated in `OnCreatingTicket` and rejected if not listed (empty = accept all).
- **FakeAuthHandler (Rule 4.4)**: header-based scheme (`X-Fake-User`, `X-Fake-Roles`) for non-interactive test auth. Registered only outside Production (`AddFakeAuth` throws `InvalidOperationException` if wired under Production); the cookie scheme forwards to it when the header is present.
- **GUEST Mode**: Allowed in dev/test (hidden + server-rejected in Production). Clicking "Login as GUEST" hits `/auth/login/fake` and creates a session with username `GUEST{random 8 digits}`. Persisted in LocalStorage. Button visibility is gated by the server-authoritative `EnableGuestMode` flag from `/api/feature-flags` (not by hostname).
- **Cookies (Rule 4.2)**: session cookie is `SameSite=Strict`; OAuth correlation cookies stay `Lax` (Strict drops them on the cross-site provider callback).
- **Production enforcement**: `ProductionAuthEnforcementMiddleware` challenges unauthenticated requests to OAuth only in Production; all non-prod environments are open.

## AI Model Selection (Rule 14)

- `GET /api/ai-models` returns the selectable model catalog grouped into three categories: **Remote** (Azure OpenAI), **Browser** (in-browser WASM), and **Ollama** (local — only included when the app is NOT running in Azure App Service, i.e. `WEBSITE_SITE_NAME` unset).
- The home page (`Repositories.razor`) renders `AiModelSelector.razor`, a grouped dropdown; the choice is persisted to `localStorage` (`selectedAiModel`) so it works for both OAuth and GUEST users.

## Versioning

- Git-driven via **MinVer** (tag prefix `v`, e.g. `v1.2.3`). No tags → `0.0.0-alpha.0.<height>`.

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

# Run tests (CI does NOT run tests — Rule 6.4)
dotnet test tests/PoRepoLineTracker.UnitTests
dotnet test tests/PoRepoLineTracker.IntegrationTests

# E2E (C#) — target a running instance via E2E_BASE_URL (default http://localhost:5000);
# tests self-skip when no instance is reachable / Playwright browsers aren't installed.
dotnet test tests/PoRepoLineTracker.E2EAPI
dotnet test tests/PoRepoLineTracker.E2EUI   # first: pwsh tests/PoRepoLineTracker.E2EUI/bin/Debug/net10.0/playwright.ps1 install
```

## Test Matrix (Rule 2.2)

`UnitTests`, `IntegrationTests`, `E2EAPI` (pure API), `E2EUI` (C# Playwright) — single stack: **xunit + NSubstitute + FluentAssertions** (Moq and NUnit removed). The legacy `E2ETests.TS` (TypeScript Playwright) remains for now but is superseded by `E2EUI` and slated for removal.

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
- Trim analyzers on `.Client`/`.Shared` (Rule 6.6): `IsTrimmable` + `EnableTrimAnalyzer` enabled; reflection-JSON + Blazor-framework diagnostics (`IL2026;IL3050;IL2110;IL2111`) suppressed via `NoWarn`, all other trim hazards still fail the build.
- Source-generated logging (Rule 6.1): `[LoggerMessage]` partial class `Application/Telemetry/AnalysisLog.cs` for the per-commit hot loop.
- Validation (Rule 2.2): FluentValidation rules in `.Shared/Validation`, registered via `AddValidatorsFromAssemblyContaining`, applied at the alert-create and bulk-add endpoints.
- All patterns documented with XML `<summary>` tags explaining WHY
- Serilog with UserId, SessionId, CorrelationId enrichment
- OpenTelemetry with ActivitySource and Meter
