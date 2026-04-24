# PoRepoLineTracker – LLM Documentation

> **Last Updated:** 2026-04-24
> **Purpose:** Quick-reference guide for coding LLMs to understand project structure, conventions, and public API surfaces.

---

## 1. Solution Overview

| Layer | Purpose |
|-------|---------|
| **PoRepoLineTracker.Domain** | Entity models, value objects, domain interfaces — zero external dependencies |
| **PoRepoLineTracker.Application** | Application services, MediatR handlers, DTOs, interfaces — depends on Domain |
| **PoRepoLineTracker.Infrastructure** | Azure Table Storage, LibGit2Sharp, GitHub API, file system access — depends on Application |
| **PoRepoLineTracker.Shared** | Models shared between Client (WASM) and Api |
| **PoRepoLineTracker.Api** | ASP.NET Core host (ports 5000/5001), middleware, endpoint mappings, DI registration |
| **PoRepoLineTracker.Client** | Blazor WebAssembly SPA, Radzen UI components |

**Onion Architecture:** Dependencies flow inward: Api → Infrastructure → Application → Domain. Domain has no external dependencies.

---

## 2. Key Configuration Files

- `global.json` — .NET SDK version pinning (`10.0.201`)
- `Directory.Build.props` — `<TreatWarningsAsErrors>true`, `<Nullable>enable`
- `Directory.Packages.props` — Central Package Management
- `docker-compose.yml` — Azurite for local Table Storage simulation

---

## 3. Project Entry Points

- `src/PoRepoLineTracker.Api/Program.cs` — Host builder, middleware pipeline, Serilog, OpenTelemetry, health checks
- `src/PoRepoLineTracker.Client/Program.cs` — Blazor WASM entry point
- `src/PoRepoLineTracker.Client/wwwroot/index.html` — SPA shell

---

## 4. API Endpoint Groups

| File | Endpoints |
|------|-----------|
| `Api/Extensions/AuthEndpoints.cs` | `/api/auth/login`, `/api/auth/logout`, `/api/auth/me` |
| `Api/Extensions/RepositoryEndpoints.cs` | `/api/repositories`, `/api/repositories/{id}`, `/api/repositories/{id}/linecounts` |
| `Api/Extensions/GitHubEndpoints.cs` | `/api/github/repositories`, `/api/github/repositories/{owner}/{repo}/statistics` |
| `Api/Extensions/SettingsEndpoints.cs` | `/api/settings`, `/api/settings/{userId}` |
| `Api/Extensions/FailedOperationEndpoints.cs` | `/api/failed-operations` |
| `Api/Extensions/DiagnosticsEndpoints.cs` | `/diag`, `/health`, `/dev-login`, `/test-login` |

---

## 5. Domain Models

| Model | Location |
|-------|----------|
| `User` | `Domain/Models/User.cs` |
| `GitHubRepository` | `Domain/Models/GitHubRepository.cs` |
| `CommitLineCount` | `Domain/Models/CommitLineCount.cs` |
| `FailedOperation` | `Domain/Models/FailedOperation.cs` |
| `UserPreferences` | `Domain/Models/UserPreferences.cs` |
| `ChartDisplayMode` | `Domain/Models/ChartDisplayMode.cs` |

---

## 6. Application Interfaces

| Interface | Purpose |
|-----------|---------|
| `IGitHubService` | GitHub API operations |
| `IRepositoryDataService` | Repository CRUD |
| `IUserService` | User management |
| `IUserPreferencesService` | User preferences |
| `ILineCounter` | Code line counting strategy |
| `IAnalysisProgressService` | Live analysis tracking |
| `IFailedOperationService` | Failed operation management |

---

## 7. Authentication

- GitHub OAuth via `AspNet.Security.OAuth.GitHub`
- Cookie authentication (`PoRepoLineTracker.Auth`)
- Managed Identity (`DefaultAzureCredential`) for Key Vault access
- Dev bypass: `/dev-login/{userId}`, `/test-login`, `/test-login-redirect`
- ANON button on login page for local development

---

## 8. Azure Infrastructure

| Resource | Details |
|----------|---------|
| App Service | Hosted in app resource group |
| App Service Plan | Shared via `PoShared` resource group |
| Azure Table Storage | App-specific resource group |
| Key Vault | `PoShared` vault, prefixed secrets |
| App Insights | `PoShared` instance aggregated |

Deployment: Bicep via Azure Developer CLI (`azd`).

---

## 9. Testing Strategy

| Test Layer | Framework | Scope |
|------------|-----------|-------|
| Unit Tests | xUnit + Moq | Domain logic, Service layers |
| Integration Tests | xUnit + Testcontainers (Azurite) | API endpoints, Repository patterns |
| E2E Tests | Playwright (TypeScript) | Critical Blazor UI paths |

---

## 10. Observability

- **Serilog** → Console (always), File (dev only), App Insights (when configured)
- **OpenTelemetry** → OTLP exporter, ASP.NET Core + HttpClient instrumentation
- **Log Context:** UserId, SessionId, Environment, CorrelationId
- **Health Checks:** `/health` (JSON), Azure Table Storage probe
- **Diagnostics:** `/diag` (requires auth), masked secrets

---

## 11. Coding Conventions

- C# 14 features (targeting `net10.0`)
- Nullable reference types enabled project-wide
- Treat warnings as errors
- SOLID/GoF patterns commented inline where used
- DTOs in Application layer, Entities in Infrastructure, Models in Domain
- No comments on self-explanatory code (constructors, simple properties)