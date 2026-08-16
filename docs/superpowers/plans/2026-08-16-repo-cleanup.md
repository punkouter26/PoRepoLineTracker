# Repo-Wide Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all verified-dead code, endpoints, assets, infra artifacts and docs; collapse unearned abstractions; standardize slice layout; right-size the test suite to ~100 Unit / ~50 Integration / ~25 E2EAPI / ~25 E2EUI — with full functional parity for everything the Client actually uses.

**Architecture:** Pure-deletion tasks first (endpoints, dead C#, infra, tooling, docs, assets, packages), then the two refactor tasks (abstraction collapse, slice standardization), then test right-sizing last so counts settle after deletions. Every task ends with `dotnet build` + Unit + Integration green and its own commit.

**Tech Stack:** net10.0, Blazor WASM + Minimal API, xunit/NSubstitute/FluentAssertions, Azurite via docker compose, Bicep + GitHub Actions.

**Spec:** The evidence base is the two-agent survey in this session (endpoint caller cross-check, package grep, test census). Where a claim matters (e.g. "no caller"), the executing step re-verifies with grep before deleting.

## Global Constraints

- `TreatWarningsAsErrors=true` — any unused-using or dead-field warning fails the build; clean up as you delete.
- Trimming ON for WASM: removing a wire type requires removing its `[JsonSerializable]` entry in `AppJsonSerializerContext`.
- Slices must not reference each other; only `Extensions/` composes.
- A running app locks the build — `Get-Process -Name PoRepoLineTracker.API -ErrorAction SilentlyContinue | Stop-Process -Force` before building.
- Verify gate per task: `dotnet build && dotnet test tests/PoRepoLineTracker.Unit && dotnet test tests/PoRepoLineTracker.Integration`. E2E tiers run once at the end (Task 12).
- The working tree has uncommitted telemetry work (TelemetrySettings resolver). Task 0 commits it separately before cleanup starts so cleanup commits stay pure.
- Before deleting any "no caller" symbol, grep `src/` and `tests/` for its name; if a caller appears that the survey missed, keep it and note the deviation.

---

### Task 0: Commit pre-existing telemetry WIP

**Files:** `CLAUDE.md`, `docker-compose.yml`, `TelemetryServiceExtensions.cs`, `DiagnosticsEndpoints.cs`, `PoRepoLineTracker.API.csproj`, `appsettings.Development.json`, `Telemetry/TelemetrySettings.cs` (untracked).

- [ ] Step 1: `git status` + `git diff` to confirm the changes are the TelemetrySettings/Jaeger work CLAUDE.md describes.
- [ ] Step 2: Build + Unit + Integration to confirm the WIP is green before committing it.
- [ ] Step 3: `git add` those files and commit: `feat(telemetry): single TelemetrySettings resolver shared by AddTelemetry and /diag; Jaeger via compose`.

### Task 1: Delete the 7 dead endpoints and their exclusive chains

**Files:**
- Modify: `Features/Diagnostics/DiagnosticsEndpoints.cs` (delete `MapDevOnlyEndpoints`, `POST /api/log/client`, `ClientLogEntry`), `Extensions/ApiEndpointExtensions.cs` (drop the call)
- Modify: `Features/Repositories/RepositoryEndpoints.cs` (delete `POST {id}/analyses`, delete `GET {id}/top-files`)
- Delete: `Features/Repositories/GetTopFilesQuery.cs`, `Features/Settings/GetConfiguredFileExtensionsQuery.cs`
- Modify: `Features/Settings/SettingsEndpoints.cs` → keep only the two `user-preferences` routes (~40 lines)
- Modify: `Storage/IRepositoryDataService.cs` + impl: remove `GetTopFilesAsync`, `GetConfiguredFileExtensionsAsync`, `SaveTopFilesAsync`; delete `TopFileEntity`
- Modify: `Services/IGitHubService.cs` + `GitHubService.cs`: remove `GetTopFilesByLineCountAsync` + `GetTopFilesByLineCountFromFullPathAsync` and call sites in `AnalyzeRepositoryCommits.cs`
- Modify: Shared: remove `TopFileDto` + its `AppJsonSerializerContext` entry; remove `ConfigKeys.ChartSettings.MaxLinesOfCode` (verify no other consumer) and `ConfigKeys.FeatureFlags.*` (verify none of the three flags is read outside the deleted endpoint — especially `EnableBackgroundAnalysis`)
- Modify: `Middleware/ProductionAuthEnforcementMiddleware.cs` + `Extensions/AuthServiceExtensions.cs`: drop `/api/feature-flags` allowlist entries and stale comments
- Tests: delete/trim every test naming a removed route (Integration `ApiEndpointTests` analyses + feature-flags tests; E2EAPI feature-flags ×2, `AuthorizationApiTests` user-extensions; Unit tests for `GetTopFilesQuery`/`GetConfiguredFileExtensionsQuery` if any)

**Guard:** `IUserPreferencesService.GetFileExtensionsAsync` — check whether `AnalyzeRepositoryCommits` calls it before deciding whether it dies with the `user-extensions` endpoint; keep the method if analysis uses it (only the endpoint is dead).

- [ ] Step 1: grep each route string across `src/` + `tests/` to re-verify no live caller.
- [ ] Step 2: delete endpoint by endpoint, chasing each exclusive chain down to storage/DTO.
- [ ] Step 3: build + Unit + Integration; fix fallout (usings, serializer context).
- [ ] Step 4: Commit `refactor(api): delete 7 endpoints nothing calls, and their exclusive handler/storage chains`.

### Task 2: Remove dead C# code (Entra fallback, dead members, dead constants)

**Files:**
- `Features/GitHub/GitHubEndpoints.cs:30` — drop the `"ms:"` guard.
- `Features/Repositories/AnalyzeRepositoryCommits.cs:119-134` — drop the `"ms:"` guard + the whole Microsoft-PAT fallback branch.
- `Storage/GitClient.cs:29-36, ~201` — drop the Microsoft-JWT throw paths.
- `tests/...Unit/Features/AnalyzeRepositoryCommitsCommandHandlerTests.cs` — delete `Handle_MicrosoftLoggedInUser_FallsBackToConfiguredGitHubPat`.
- `Storage/IUserService.cs` + `UserService.cs` — delete `DeleteUserAsync`, `UpdateAccessTokenAsync`, `GetAccessTokenAsync`; move `GetUserByGitHubIdAsync` off the interface (private helper if only used internally).
- `Client/Services/AppHttpJsonExtensions.cs` — delete `PostAppJsonAsync` (verify the doc-comment mention of `ListTopFileDto` went with Task 1).
- `Shared/Models/ConfigKeys.cs` — delete `Telemetry.AppInsightsConnectionStringSection`, `Telemetry.AppInsightsInstrumentationKey`, `Telemetry.AppInsightsInstrumentationKeySection`.
- `Program.cs:88` — delete the unused `appInsightsConn` local.
- `Telemetry/AppTelemetry.cs` + `Program.cs:106-108` — delete the two always-zero observable gauges and `InitializeGauges` (or its zero-wired parameters if other gauges are real).

- [ ] Step 1: grep each symbol for callers before deleting.
- [ ] Step 2: delete; build + Unit + Integration.
- [ ] Step 3: Commit `refactor: remove unreachable Entra fallback paths and dead members/constants/gauges`.

### Task 3: Delete/fix broken infra artifacts

**Files:**
- Delete: `src/PoRepoLineTracker.API/Dockerfile`, root `.dockerignore`, `infra/main.bicepparam`, `azure.yaml`.
- Modify: `infra/resources.bicep` — remove the `AzureTableStorage__FailedOperationTableName` app setting (lines ~142-144); change `appCommandLine` to `'/home/site/wwwroot/startup.sh'` (bicep becomes the single owner).
- Modify: `.github/workflows/deploy.yml` — delete the "Ensure startup command runs startup.sh" step (~line 141).

- [ ] Step 1: apply; `az bicep build --file infra/main.bicep` if az available, else careful read.
- [ ] Step 2: Commit `ci(infra): single owner for appCommandLine, drop dead FailedOperation setting, remove unbuildable Dockerfile and orphaned azd files`.

### Task 4: Repair broken dev tooling

**Files:**
- `.vscode/tasks.json` / `.vscode/launch.json` — fix `PoRepoLineTracker.sln` → `PoRepoLineTracker.slnx`, `src/PoRepoLineTracker.Api` → `.API` (3 paths + program dll name).
- `tests/coverlet.runsettings` — Include `[PoRepoLineTracker.API]*,[PoRepoLineTracker.Shared]*`, Exclude the real test assembly names; drop the Migrations comment.
- `tests/README.md` — state coverage is collected only when run with `--collect:"XPlat Code Coverage"` (not "enforced").
- `src/PoRepoLineTracker.API/api-tests.http` — remove dead requests (`/dev-login`, `/test-login`, single-add POST, failed-operations ×2, plus any Task-1 casualties); add the `X-Fake-User` header pattern and an antiforgery GET + `X-CSRF-TOKEN` example for writes.
- `SCRIPTS/setup.ps1` — rename `$pid` loop var → `$procId`; fix printed path to `src/PoRepoLineTracker.API`; mention Jaeger in the compose message.
- `SCRIPTS/README.md` — delete the phantom `patch-azurite.ps1` row + usage block; mention Jaeger.

- [ ] Step 1: apply all; sanity-run `pwsh -NoProfile -Command { . nothing }`-level syntax check on setup.ps1 (`pwsh -NoProfile -c "Get-Command -Syntax"` not needed — just `pwsh -NoProfile -File SCRIPTS/setup.ps1 -WhatIf` is not supported; instead parse: `[System.Management.Automation.Language.Parser]::ParseFile`).
- [ ] Step 2: Commit `chore(tooling): fix rotted .vscode/coverlet/api-tests/setup.ps1 references`.

### Task 5: Rewrite stale docs

**Files:**
- `README.md` — remove: `/dev-login` section, failed-operations claims, single-add wording ("bulk only"), ACR mention, `azd` deploy section (replace with "deploys via GitHub Actions on push to master"); fix `src/PoRepoLineTracker.Api` casing; drop links to deleted MultiplayerFlow diagrams.
- Delete: `docs/MultiplayerFlow.mmd`, `docs/MultiplayerFlow_SIMPLE.mmd`, `docs/RefactorBlastRadius.md`.
- `docs/PoIdeas.md` — prepend a header: written pre-removal of AiDetection/CommitTagger/SmartAlerts; type names inside no longer exist; "no AI in this app" rule overrides line 15.
- Sweep `Rule X.Y` citations: grep `Rule [0-9]` in `src/` + `tests/`; delete the citation prefix, keep each comment's inline rationale.
- `NET_RULES.md` — remove the user-secrets prohibition in §2.3 (contradicts CLAUDE.md's documented setup) or record the deviation; then update the CLAUDE.md paragraph that describes the numbering divergence to say citations were removed.

- [ ] Step 1: apply; re-grep `dev-login|failed.operation|azd|Rule [0-9]` to confirm the sweep.
- [ ] Step 2: Commit `docs: remove claims about deleted features; retire Rule X.Y citations`.

### Task 6: Prune orphan assets and dead front-end code

**Files:**
- `Client/wwwroot/manifest.json` — add `icon-192.png` to `icons` (real 192px PWA icon; better than deleting).
- `Client/wwwroot/css/effects.css` — delete `.u-grad-mesh` (~125-146), `@keyframes meshDrift` (~148-151), its `prefers-reduced-motion` guard (~285); fix the header comment that wrongly claims `.u-sheen` was removed.
- `Client/wwwroot/index.html` — delete `clickGitHubButton()` (~60-68).
- Delete from disk (untracked): `src/PoRepoLineTracker.API/log20260815.txt`, `log20260816.txt`.
- `.gitignore` — dedupe the ~23 repeated lines; delete stale entries (`E2ETests.TS` ×4, `apphost_resources.json`, `IBRPMIS.zip`, wrong-case `src/PoRepoLineTracker.Api/log*.txt`).

- [ ] Step 1: apply; grep `u-grad-mesh|clickGitHubButton` → 0 hits (except PageHero.razor.css prose comment — reword it).
- [ ] Step 2: Commit `chore(assets): PWA icon into manifest, drop dead CSS/JS, clean .gitignore`.

### Task 7: Prune unused packages

**Files:** `Directory.Packages.props`, `src/PoRepoLineTracker.API/PoRepoLineTracker.API.csproj`, test csprojs.

- Remove versions with zero `PackageReference`: `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.Extensions.Logging.Abstractions`, `OpenTelemetry.Instrumentation.Runtime`, `Microsoft.AspNetCore.Authentication.Cookies`, `Microsoft.CodeAnalysis.NetAnalyzers` (verify each with grep first).
- Remove `Radzen.Blazor` PackageReference from the API csproj (transitive via Client ProjectReference).
- `Xunit.SkippableFact`: grep `SkippableFact|Skip.If` per test project; remove the reference from tiers with zero usage.
- `Microsoft.Extensions.Configuration` in Unit/Integration csprojs: remove; restore only if build breaks.
- Console exporter: if `EnableConsoleExporters` code path exists in `TelemetryServiceExtensions.cs` and every shipped config says false, remove the path + `OpenTelemetry.Exporter.Console` package; otherwise leave.

- [ ] Step 1: apply removals one group at a time; full build + Unit + Integration after each group.
- [ ] Step 2: Commit `chore(deps): remove unreferenced and redundant packages`.

### Task 8: Collapse unearned abstractions

**Files:**
- Delete `Storage/IGitClient.cs`; register `GitClient` concrete; `GitHubService` ctor takes `GitClient`. Convert `GitClient.cs` to file-scoped namespace while touching it.
- Delete `Storage/IFileIgnoreFilter.cs`; register + inject concrete `FileIgnoreFilter`.
- `IUserPreferencesService`: check `CustomWebApplicationFactory.cs:257` first — if the factory substitutes the interface for a load-bearing reason, keep it and skip; otherwise delete interface, inject concrete.
- `ILineCounter` registrations: replace the 17 `AddScoped` lines with a static catalog `SourceLineCounter.DefaultSet()` returning `IReadOnlyList<ILineCounter>` built from a `(ext, lineComment, blockComment)` table; register once. Keep the interface (it is genuinely consumed as `IEnumerable<ILineCounter>`). Convert `Services/ILineCounter.cs` to file-scoped namespace; fix its doc reference to the nonexistent `DefaultLineCounter`.
- `GitHubService.cs` dedup: read both members of each `…Async` / `…FromFullPathAsync` pair (`GetCommitStatsAsync`, `CountLinesInCommitAsync`, `IsRepositoryValidAsync` — top-files pair died in Task 1). If bodies differ only in how the repo is opened, extract a private core method taking the opened repo handle; both publics become open-then-delegate.

- [ ] Step 1: one abstraction at a time, building between each.
- [ ] Step 2: full Unit + Integration.
- [ ] Step 3: Commit `refactor: drop single-impl interfaces, collapse line-counter registrations, dedupe GitHubService open-variants`.

### Task 9: Standardize slice layout and naming

**Files:**
- Rename `Features/Repositories/GetFileExtensionPercentages.cs` → `GetFileExtensionPercentagesQuery.cs`, `GetAllRepositoriesLineCountHistory.cs` → `GetAllRepositoriesLineCountHistoryQuery.cs` (convention: queries carry `Query` suffix, commands are bare verbs). Rename contained types to match if they lack the suffix.
- Move `Features/Repositories/AnalysisProgressService.cs` + `IAnalysisProgressService.cs` → `Services/` (namespace update + registration reference).
- Client: delete `Pages/Settings.razor` pass-through; move `@page "/settings"` + `@page "/settings/extensions-counted"` + the PageHero onto `ExtensionsCounted.razor` (keep its existing route too if distinct); update NavMenu link target if needed. Move `AddRepository.razor`, `UploadRepository.razor`, `ExtensionsCounted.razor` stay in place (embedded-as-component pattern is documented; only fix if trivial).

**Guard:** E2EUI navigates `/settings` — keep that route answering with the same heading text.

- [ ] Step 1: apply; grep old type/namespace names → 0.
- [ ] Step 2: build + Unit + Integration.
- [ ] Step 3: Commit `refactor: consistent query naming, progress service out of the slice, single settings page`.

### Task 10: Right-size Unit + Integration tests (~100 / ~50)

**Unit (post-deletion count → target ~100):**
- `FileIgnoreFilterTests.cs`: merge the seven `ShouldIgnoreDirectory_*_ReturnsTrue` theories into one `ShouldIgnoreDirectory_IgnoredPaths_ReturnsTrue` and the false-cases into one, keeping one representative `InlineData` per rule family (root-marker content rule keeps its distinct cases — it's the load-bearing rule). Target ≤8 methods / ≤25 cases.
- Delete `StronglyTypedIdTests` compiler-behavior tests (`New_ProducesDistinctIds`, `Empty_IsTheZeroGuid`, `ToString_MatchesUnderlyingGuid`, `SameGuid_ProducesEqualIdsOfTheSameType`) and both self-referential `ConfigKeysTests`.
- `EntityMappingTests.cs`: one round-trip test per mapper (3 total).
- Trim per-property micro-tests in `GetPortfolioInsightsQuery` (20) and `UploadEndpoints` helpers (11) to behavior-level tests.

**Integration (~75 → ~50):**
- Health: keep `Health_Endpoint_Returns_200` in `ApiEndpointTests`; delete the other three.
- `/diag` masking: keep the `FakeAuthAndDiagTests` trio; delete `ApiEndpointTests.Diagnostics_Endpoint_Masks_Secret_Values`.
- `ExceptionMiddlewareTests`: collapse 5 → 1 test asserting status, content-type, title, detail in one pass.
- Delete `AzureTableIntegrationTests.cs` (template scaffolding) — but first check whether `AzuriteFixture`/`Testcontainers.Azurite` serve other tests; if not, remove those too.
- `/auth/me` anonymous: keep one per tier.

- [ ] Step 1: apply Unit trims; run Unit; count cases (`dotnet test ... --list-tests | Measure-Object`).
- [ ] Step 2: apply Integration trims; run Integration; count.
- [ ] Step 3: Commit `test: right-size Unit and Integration to core critical paths`.

### Task 11: Right-size E2E tiers (~25 / ~25)

**E2EAPI (~42 → ~25):** drop duplicate health (keep 1), auth-me duplicates, feature-flags tests (endpoint gone in Task 1), keep auth-refusal sweep, CSP/routing/verbs, static-asset checks.

**E2EUI (~66 → ~25):** parameterize with `[Theory]` over viewport (mobile/desktop) and route:
- one `NoHorizontalScroll` theory replaces 5 tests; one `TapTargets` theory replaces 4; one `ShellRenders` theory replaces 4; one `Theme` theory (light/dark/system) replaces 5.
- Keep: chart rendering with seeded data, accessibility core checks, sidebar toggle (1), login page (2), scoped-CSS smoke (1).

- [ ] Step 1: apply; compile only (`dotnet build tests/...`); actual runs happen in Task 12.
- [ ] Step 2: Commit `test: consolidate E2E tiers into parameterized critical-path suites`.

### Task 12: Full verification

- [ ] Step 1: kill any running API process; `dotnet build`.
- [ ] Step 2: `dotnet test tests/PoRepoLineTracker.Unit` and `tests/PoRepoLineTracker.Integration` (Docker must be up for Testcontainers if still used).
- [ ] Step 3: `docker compose up -d`; start API (`dotnet run --project src/PoRepoLineTracker.API --launch-profile https`, background); wait for `https://localhost:5001/health`.
- [ ] Step 4: `dotnet test tests/PoRepoLineTracker.E2EAPI` then `tests/PoRepoLineTracker.E2EUI` (browsers installed if first run). Zero skips expected.
- [ ] Step 5: stop the app; report final counts per tier vs targets; final commit if fixes were needed.
