# PoRepoLineTracker — Agent Context

## Project Overview

PoRepoLineTracker is a Blazor WASM + ASP.NET Core application that tracks lines of code across GitHub repositories. It provides AI code detection, contributor statistics, and line trend analysis. Users authenticate via GitHub OAuth only.

## Architecture

**Vertical Slice Architecture** across exactly three projects (Rule 2):

```
PoRepoLineTracker.Shared   → DTOs, enums, strongly-typed IDs, domain models, FluentValidation (LEAF — no project refs)
PoRepoLineTracker.API      → Minimal API host + BFF + storage + feature slices; also serves the WASM client
PoRepoLineTracker.Client   → Blazor WASM UI (depends on Shared only)
```

Inside `.API`, directories stay at most two deep:

```
API/
├── Features/{Name}/   # endpoints + commands/queries + handlers, co-located (one file per operation)
├── Storage/           # Azure Table entities + data services
├── Services/          # cross-slice services (AI detection, line counting)
├── Auth/              # FakeAuthHandler
├── Middleware/        # exception handling, security headers, log enrichment
├── Telemetry/         # ActivitySource, Meters, source-generated logging
└── Extensions/        # composition root
```

**Slice autonomy**: slices must not reference each other. `GlobalUsings.cs` deliberately imports
`Storage`/`Services`/`Telemetry` but NOT `Features.*`, so cross-slice coupling requires an explicit
`using` — only the composition root (`Extensions/`) has one. A query used by exactly one slice lives
in that slice (e.g. `GetContributorStatsQuery` is in `Features/AiDetection`).

**Key Rule**: The Client project must NOT reference API internals. It communicates over HTTP.

**Shared is the leaf (Rule 2.2)**: `.Shared` holds DTOs and FluentValidation rules only and has **no project references**. The shared-contract enums (`AlertMetric`, `AlertOperator`, `ChartDisplayMode`) physically live in `.Shared` (`Models/SharedContractEnums.cs`) but keep the `PoRepoLineTracker.Domain.Models` namespace so existing `using` directives resolve unchanged; `.Domain` references `.Shared` to consume them. DTOs never embed Domain entities — wire shapes like `GitHubRepositoryDto` mirror them instead.

## Naming Convention

- Master prefix: `PoRepoLineTracker` for all namespaces, Azure resources, and configuration keys
- Key Vault secrets use `PoRepoLineTracker--` prefix (e.g., `PoRepoLineTracker--GitHub--ClientId`)
- Table names use `PoRepoLineTracker` prefix (e.g., `PoRepoLineTrackerRepositories`)

## Authentication

- **Routes**: `/auth/login`, `/auth/logout`, `/auth/me`.
- **GitHub OAuth**: the ONLY interactive provider (AspNet.Security.OAuth.GitHub). Microsoft/Entra sign-in was removed — see the deviation note under Azure Deployment.
- **Dev/Test auth (Rule 3.3)**: `Auth/FakeAuthHandler` authenticates from `X-Fake-User` (GUID used verbatim, any other string hashed to a stable GUID) and `X-Fake-Roles` (comma-separated). Outside Production the default scheme is a policy scheme (`FakeOrCookie`) that forwards to the fake handler only when `X-Fake-User` is present, so cookie/OAuth is untouched otherwise. `FakeAuthHandler.ThrowIfProduction` makes registering it in Production a startup crash. The old `/dev-login/{userId}` route is gone.
- **No guest/anonymous login**: the GUEST dev-bypass and `IsAnon`/`EnableGuestMode` plumbing were removed. Interactive login is GitHub or Microsoft OAuth only.
- **Antiforgery cookie**: `Security:RequireSecureCookies` (default true outside Development) decides `__Host-` + `Secure` vs. unprefixed + `SameAsRequest`. NOT keyed on the environment name — the integration tier runs as `"Test"` over plain HTTP and must opt out, or antiforgery throws on every request. Read at registration time, so it must arrive via `UseSetting`, not `ConfigureAppConfiguration`.
- **Cookies**: session + correlation cookies are `SameSite=Lax`. (Rule 4.2 names `Strict`, but `Strict` breaks interactive OAuth sign-in — the browser withholds the freshly-set cookie on the post-login redirect, causing a "pick an account" loop. `Lax` is the correct value for an OAuth session cookie.)
- **Production enforcement**: `ProductionAuthEnforcementMiddleware` challenges unauthenticated requests to OAuth only in Production; all non-prod environments are open.

## Diagnostics

- **`/diag`** (Rule 3.2) is content-negotiated and `[Authorize]`-protected. `Accept: text/html` (a browser) gets the Blazor shell so `Pages/ExternalConnections.razor` renders; anything else gets JSON from `DiagnosticsEndpoints.BuildMaskedConfiguration` — every secret reported as `configured` + a `****last4` hint, never a usable value. Non-secret settings are returned verbatim.
- **`GET /api/diagnostics`** remains the richer health + external-connection JSON the client page consumes.

## Removed features

- **Microsoft/Entra sign-in** — a Microsoft principal carries no GitHub credential, so it could sign in but not read a repository. See the deviation note under Azure Deployment.
- **WebGL backdrop + Web Audio feedback** — `gfx.js`, `audio.js`, `GfxService`, `AudioService`, `SoundSettingsCard`, and the unused `.u-grad-*`/`.u-sheen`/`.u-press`/`.u-stagger`/`.u-glass-strong` utilities. ~1,900 lines, none of it on any data path. The login backdrop is now the CSS blobs that were already there as the fallback.
- **Commit tagging** — `CommitTaggerService`, the `TagsJson` column, and `Tags` on `CommitLineCount`/`CommitStatsDto`/`DailyLineCountDto`. This also removed the badge row it fed on the repository detail page's timeline scrubber. Azure Tables is schemaless, so pre-existing rows keep the property and it is ignored.
- **`POST /api/repositories`** (single add) and the dev `fix-local-path` route. `/api/repositories/bulk` is the only write path — it dedupes via `GetRepositoryByOwnerAndNameAsync`, validates through the shared FluentValidation rules, and queues analysis only for what it actually added; the single-add path did none of that and inserted unconditionally, so adding the same repository twice produced two rows.
- **AI model selector** (dropdown / `GET /api/ai-models` / `AiModelDto`) — was cosmetic, wired to nothing. The heuristic `AiDetectionService` remains but is not invoked; no LLM is used.
- **SmartAlerts** — all alert rules/triggers UI, endpoints (`/api/alerts/*`), `IAlertService`/`AlertService`, domain models, DTOs, and the `AlertMetric`/`AlertOperator` enums were removed.
- **Failed Operations** — the whole slice was removed: the `/failed-operations` page + nav link, all `/api/failed-operations*` endpoints, `IFailedOperationService`/`FailedOperationService`, the (stub, non-functional) `FailedOperationBackgroundService`, the `FailedOperation` domain model, `FailedOperationEntity`, and the `AzureTableStorage:FailedOperationTableName` / `FeatureFlags:EnableFailedOperationRetry` config keys. Commit-processing failures now surface via the Serilog error log **plus** the `AppTelemetry.FailedOperations` OpenTelemetry counter (`operations.failed`), incremented in the analysis handler's per-commit catch block — no dead-letter persistence.

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

# Run the API (hosts Blazor WASM). Use the https profile — SecurityHeadersMiddleware sends
# upgrade-insecure-requests, so the E2E tiers cannot drive the http profile.
dotnet run --project src/PoRepoLineTracker.API --launch-profile https

# A running instance LOCKS the build output (MSB3027). Stop it before rebuilding:
#   Get-Process -Name PoRepoLineTracker.API | Stop-Process -Force

# Run tests (CI runs the Unit tier; Integration/E2E run locally)
dotnet test tests/PoRepoLineTracker.Unit
dotnet test tests/PoRepoLineTracker.Integration

# E2E (C#) — target a running instance via E2E_BASE_URL (default https://localhost:5001);
# tests self-skip when no instance is reachable / Playwright browsers aren't installed.
dotnet test tests/PoRepoLineTracker.E2EAPI
dotnet test tests/PoRepoLineTracker.E2EUI   # first: pwsh tests/PoRepoLineTracker.E2EUI/bin/Debug/net10.0/playwright.ps1 install
```

## Test Matrix (Rule 2.2)

`Unit`, `Integration`, `E2EAPI` (pure API), `E2EUI` (C# Playwright) — single stack: **xunit + NSubstitute + FluentAssertions**. The legacy `E2ETests.TS` TypeScript Playwright project has been deleted; `E2EUI` supersedes it.

Current counts (test cases, Theory rows included): **Unit 214 · Integration 76 · E2EAPI 62 · E2EUI 64** against targets of 100/50/25/25. All four tiers pass with **zero skips**.

E2EUI seeds its own chart data through `POST /api/dev/seed/repository` (Development only) — before
that, twelve chart assertions skipped themselves on every run and the tier reported false health.
A skip there now means the app is not reachable at `https://localhost:5001`, or is not in Development.

## Key Patterns

- **GoF Facade**: `ApiEndpointExtensions` unifies all endpoint registration
- **GoF Template Method**: `PrefixKeyVaultSecretManager` customizes secret loading
- **GoF Strategy**: Feature flags (`/api/feature-flags`) drive runtime UI toggles like the GitHub login button
- **SOLID — DIP**: All services registered by application-layer interfaces
- **SOLID — OCP**: Auth providers are additive without modifying existing ones
- **SOLID — ISP**: Each endpoint group has its own mapping method (VSA)
- **Composition Root**: `InfrastructureServiceExtensions` centralizes all DI
- **Vertical Slice Architecture**: each feature owns its folder under `API/Features/`, holding its endpoints, commands/queries and handlers together

## Azure Deployment

- **Startup command must be `/home/site/wwwroot/startup.sh`** (set automatically by the deploy
  workflow). The DOTNETCORE image has no `git`, and `GitClient` shells out to it for clone/pull,
  so without the script every analysis fails with "git is not installed or not in PATH".
  The script also resolves the entry assembly by discovery, which keeps the `.Api` -> `.API`
  rename from breaking the boot on case-sensitive Linux.
- **OAuth**: GitHub is the ONLY provider. The GitHub OAuth App (`Ov23liKNXw9OAOkwgDNG`) must have
  `https://app-porepolinetracker.azurewebsites.net/signin-github` registered as its callback —
  that lives on github.com, not in Azure.
  - **DEVIATION from NET_RULES 3.3** ("Entra ID OAuth uses the `/common` endpoint with a
    server-side `FallbackPolicy`"): Microsoft/Entra sign-in was removed. This app exists to read
    GitHub repositories and a Microsoft principal carries no GitHub credential. The handler stored
    the Microsoft access token in `User.AccessToken` — documented as *"GitHub OAuth access token…
    used to access GitHub API on behalf of the user"* — and a synthetic `ms:{oid}` in
    `User.GitHubId`. `GitHubService.GetUserRepositoriesAsync` then sent that Microsoft token to
    `api.github.com` as a Bearer credential, which 401s, so "Select from GitHub" failed with no
    explanation for anyone who used that button. The `FallbackPolicy` half of 3.3 is unaffected
    and still enforced. The Microsoft app registration `PoRepoLineTracker-WebAuth`
    (`7db2fe07-0780-41d0-b11f-3712297d4daf`) is now unused by this app.
  - **Local development needs its own OAuth App.** A GitHub OAuth App has exactly one callback
    URL and GitHub matches `redirect_uri` on host *and* port, so the production app can never
    authorize `http://localhost:5000/signin-github` — the browser gets GitHub's "The redirect_uri
    is not associated with this application". Create a second OAuth App pointing at
    `http://localhost:5000/signin-github` and put its ClientId/Secret in
    `src/PoRepoLineTracker.API/appsettings.Development.local.json` (gitignored; loaded after Key
    Vault, so it overrides prod locally and changes nothing about the deployment). See
    `appsettings.Development.local.json.template` for the exact steps. GitHub exposes **no API**
    for creating OAuth Apps and `gh` has no command for it, so this step is browser-only.

- Resource group: `PoShared` (shared services) + `PoRepoLineTracker` (app resources)
- Key Vault: `kv-poshared` in PoShared RG
- Storage: `stporepolinetracker` in PoRepoLineTracker RG
- App Service Plan: `asp-poshared-linux` in PoShared RG
- App Insights: shared instance in PoShared RG

## Coding Rules

- `LangVersion=preview` — the target is C# 15, but SDK 10.0.301's Roslyn rejects `15.0` (C# 14 is its newest numbered version). `preview` tracks the newest the installed compiler supports; pin to `15.0` once an SDK implementing it is in `global.json`.
- `TreatWarningsAsErrors=true` globally
- `Nullable=enable` globally
- AOT disabled (`IsAotCompatible=false`)
- Trimming ENABLED on the WASM client (`PublishTrimmed=true`, `IsTrimmable=true`, `EnableTrimAnalyzer=true` on `.Client` and `.Shared`). This reverses the previous deviation. The blocker was never Blazor's component activation on its own — it was reflection-based JSON: with the reflection resolver reachable, the trimmer had to keep member metadata alive for anything serializable, and the published app died at runtime with `CtorNotLocated` (MainLayout). `AppJsonSerializerContext` removed that resolver from the client entirely, and `Client/TrimmerRoots.xml` preserves both app assemblies so component activation and `[Parameter]` binding stay intact. Verified: publish runs ILLink with zero trim warnings, and 44 Playwright UI tests pass against the trimmed output (including `BlazorShell_Boots` and the desktop/mobile shell renders).
  - `IL2110`/`IL2111` are suppressed in `.Client.csproj` — they are raised against Razor-generated code for `<Router NotFoundPage>` and `<LayoutView Layout>`, which this project does not author. The suppression is only sound because `TrimmerRoots.xml` keeps those members; remove the roots and the suppression must go with them.
  - AOT remains disabled (`IsAotCompatible=false`) — a separate switch, with a large build cost and no rule requiring it.
- Source-generated logging (Rule 6.1): `[LoggerMessage]` partial class `API/Telemetry/AnalysisLog.cs` for the per-commit hot loop.
- Validation (Rule 2.2): FluentValidation rules in `.Shared/Validation`, registered via `AddValidatorsFromAssemblyContaining`, applied at the bulk-add endpoint.
- All patterns documented with XML `<summary>` tags explaining WHY
- Serilog with UserId, SessionId, CorrelationId enrichment
- OpenTelemetry with ActivitySource and Meter
