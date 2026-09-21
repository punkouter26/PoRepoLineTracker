# PoRepoLineTracker — SPEC.md

Durable reference for what this app is, what it does, what it won't do, and what must remain true at every commit.
The cross-agent *how* (YAGNI ladder, validation/security/a11y invariants) lives in `AGENTS.md` and `.github/copilot-instructions.md`. This document is the *why* and the *contract*.

---

## 1. Objective

PoRepoLineTracker is a self-hosted **GitHub repository analytics** app. A user signs in with GitHub, selects repositories they own, and the app clones each one, analyses its commit history, persists derived metrics, and surfaces line-count trends, extension breakdowns, contributor statistics, and a portfolio digest. The deployment model is one binary: the ASP.NET Core API hosts the Blazor WebAssembly client itself, talks to Azure Table Storage for state and to Azure Key Vault for secrets, and emits telemetry to Application Insights (Jaeger via OTLP locally). External surface is **GitHub + Azure** only — no SaaS analytics, no AI/ML, no alternative identity providers.

## 2. User Journeys

The single primary journey optimises every page:

1. **Sign in** — `/auth/login` → GitHub OAuth → application cookie. In Dev/Test, tools authenticate by sending `X-Fake-User`; no dev-login route.
2. **Bulk-add owned repositories** — `/api/repositories/bulk` is the only write path. Single-add was removed because it did not dedupe.
3. **Watch analysis** — background clone/pull + line counting pushes progress over `/hubs/analysis` (SignalR); a fallback poll synthesises the same frames when the hub is unreachable. UI shows live tallies (`LinesCounted`, throughput, ETA), never a percentage alone.
4. **Webhook-driven auto re-analysis** *(added 2026-09-20)* — A push to a tracked repo's default branch, signed with the configured `GitHub:WebhookSecret`, hits `POST /api/webhooks/github` and re-queues `AnalyzeRepositoryCommitsCommand` on a background task. The user doesn't take an action; the dashboard simply shows the new analysis in progress.
5. **Read the dashboard** — `Repositories` page lists the portfolio with totals. Drill into `RepositoryDetail` for line history, extension percentages, contributor stats, **code health (radar + cards)**, **activity-rhythm punchcard**, recent activity.
6. **Export the portfolio** *(added 2026-09-20)* — `GET /api/repositories/export` returns JSON by default; `?format=csv` returns `text/csv`. Discoverable endpoint, no client affordance in the danger zone yet (see §13.7).
7. **See what changed since last visit** — `Insights` page shows the digest banner (12h–90d window; trailing-7d fallback outside that), the recap (calendar-year edges), and language drift measured in **share, not lines**.
8. **Survive a network drop** *(added 2026-09-20)* — The PWA shell is cached; the `OfflineIndicator` shows network status when the connection is gone; on reconnect, the indicator clears and the next server fetch resumes. Read-only when offline.

Secondary paths: settings (`/api/settings/user-preferences` for counted extensions), diagnostics (`/diag` + `/api/diagnostics`), PWA install (`beforeinstallprompt` event captured at boot), re-analyse a single repo.

## 3. Pinned tech stack (versions)

- **.NET 10** SDK (`global.json`: `10.0.100`, `rollForward: latestMinor`, no prerelease). `<LangVersion>preview</LangVersion>` to follow the SDK forward.
- **Blazor WebAssembly** client, `<PublishTrimmed>true</PublishTrimmed>`, `<IsTrimmable>true</IsTrimmable>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`. AOT off (separate switch; no rule requires it).
- **ASP.NET Core 10** minimal-API host (`WasmBlazorWebAssemblyHost=true`). Scalar UI in Dev for the OpenAPI surface.
- **Centralised package management** — all versions in `Directory.Packages.props`; no `<Version>` on `<PackageReference>`.
- **Compiler guards** — `Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`.
- **Git-driven versioning** — MinVer 6.0.0 derives `AssemblyVersion`/`FileVersion`/`InformationalVersion` from the nearest `v*` tag.
- **Key runtime packages** — Radzen.Blazor 8.4.2 (UI), MediatR 14.1.0 (slice handlers), FluentValidation 12.0.0, HybridCache 10.0.0, OpenTelemetry 1.15.3 + OTLP exporter, Serilog.AspNetCore 10.0.0, Azure.Data.Tables 12.11.0, Azure.Identity 1.21.0, Azure.Extensions.AspNetCore.Configuration.Secrets 1.5.1, Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0, Microsoft.AspNetCore.SignalR.Client 10.0.5, LibGit2Sharp 0.31.0, Microsoft.CodeAnalysis.CSharp 4.14.0 (syntax-only, no Workspaces.MSBuild), AspNet.Security.OAuth.GitHub 9.3.0, Scalar.AspNetCore 2.13.20, Microsoft.OpenApi 2.7.5.
- **Test stack — single stack** — xunit 2.9.3 + NSubstitute 5.3.0 + FluentAssertions 8.8.0 + Xunit.SkippableFact 1.5.23 + Microsoft.Playwright 1.55.0 + Testcontainers.Azurite 4.6.0. SSH.NET pinned past advisory GHSA-q939-rpr3-3284 via central packages.
- **Local dependencies** — Docker Compose: Azurite (table 10002, blob 10000, queue 10001) + Jaeger all-in-one 1.62.0 (UI 16686, OTLP/gRPC 4317, OTLP/HTTP 4318). Start with `docker compose up -d`.

## 4. Build / Test / Lint / Run commands

```powershell
# build (every PR)
dotnet build PoRepoLineTracker.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary

# publish (what CI ships)
dotnet publish src/PoRepoLineTracker.API /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary

# tests, in dependency order
dotnet test tests/PoRepoLineTracker.Unit          # pure logic — required to pass before commit
dotnet test tests/PoRepoLineTracker.Integration   # Azurite via Testcontainers
dotnet test tests/PoRepoLineTracker.E2EAPI        # running host required
dotnet test tests/PoRepoLineTracker.E2EUI         # Playwright; first run needs `playwright.ps1 install`

# local run (https profile; Azurite must be up)
dotnet run --project src/PoRepoLineTracker.API --launch-profile https
```

**CI gate** — Unit only (commit `845c024`). Integration/E2E run on demand or locally. A skip in E2EUI means the app is not up, not that a fixture is missing.

## 5. Project structure

```
PoRepoLineTracker.slnx            ← solution
Directory.Build.props             ← TreatWarningsAsErrors, Nullable, LangVersion, MinVer
Directory.Packages.props          ← every package version (central management)
global.json                       ← .NET 10.0.100 SDK pin
docker-compose.yml                ← Azurite + Jaeger

src/
  PoRepoLineTracker.API/          ← Minimal-API host; hosts the WASM client; vertical-slice Features/
    Program.cs                    ← composition root + middleware order
    Auth/                         ← GitHub OAuth + FakeAuthHandler (throws in Production)
    Hubs/                         ← SignalR AnalysisHub (outside /api by design)
    Middleware/                   ← ApiNotFound, Antiforgery, ProductionAuthEnforcement, SecurityHeaders, ExceptionHandling, LogEnrichment
    Storage/                      ← Azure Data Tables repositories (RepositoryDataService, UserService, UserPreferencesService, CodeHealthSnapshotStore)
    Analysis/                     ← GitHubService (git CLI clone/pull), FileIgnoreFilter, SourceLineCounter
    Services/                     ← cross-slice helpers (TelemetrySettings resolver, etc.)
    Extensions/                   ← AddInfrastructure, AddAuth, AddTelemetry
    Platform/                     ← shared Po liveness, config keys
    Features/                     ← vertical slices, one folder per slice
      Antiforgery/                ← /api/antiforgery/token + middleware
      Auth/                       ← /auth/login, /auth/me, /signin-*, /signout-*
      CodeHealth/                 ← /api/code-health/{id} (six line-oriented factors; markup band separate)
      Contributors/               ← contributor stats weighted by lines added
      Webhooks/                   ← /api/webhooks/github (HMAC-verified push receiver; added 2026-09-20)
      Dev/                        ← /api/dev/seed/repository (Development only)
      Diagnostics/                ← /diag, /api/diagnostics
      GitHub/                     ← /api/github/user-repositories
      Insights/                   ← /api/insights/portfolio, /api/insights/digest, /api/insights/digest/seen
      Recap/                      ← /api/recap/{year} (calendar edges; language drift in share)
      Repositories/               ← bulk add, list, charts, linehistory, extension %, contributors, analysis, re-analyse, delete, delete-all
      Settings/                   ← /api/settings/user-preferences
    Telemetry/                    ← OpenTelemetry wiring + Application Insights

  PoRepoLineTracker.Client/       ← Blazor WASM (Radzen + scoped .razor.css)
    App.razor                     ← Router + CascadingAuthenticationState
    Layout/                       ← MainLayout (skip link via JS FocusAsync, not fragment nav)
    Pages/                        ← Login, Repositories, RepositoryDetail, CodeHealth, ExtensionsCounted, ExternalConnections, Insights
    Components/                   ← AllReposComparisonChart, AnalysisActivityFeed, ContributorChart, Repositories/* (incl. CodeHealthRadar, CommitActivityHeatmap, RepositoriesGrid), Shared/* (incl. OfflineIndicator)
    Services/                     ← ApiAuthenticationStateProvider, RepositoryCommandClient, UserPreferencesClient, AntiforgeryHandler, AnalysisFeedClient, AnalysisWatcher, AppHttpJsonExtensions
    wwwroot/                      ← service-worker.js (dev no-op), service-worker.published.js (real cache, server-route prefix exceptions)

  PoRepoLineTracker.Shared/       ← DTOs, enums, interfaces, source-generated JSON contexts
    Domain/                       ← GitHubRepository, CommitLineCount, CommitStreaks, RepositoryTotals (single definition of "Total Lines"), StronglyTypedIds, User, UserPreferences
    Models/                       ← AnalysisProgressDto, AuthResponse, ConfigKeys, ErrorResponse, Dtos/
    Serialization/                ← AppJsonSerializerContext (trim-safe, reflection resolver unreachable from client)
    Validation/                   ← FluentValidation rules

tests/
  PoRepoLineTracker.Unit/         ← pure logic; the only CI-gate tier
  PoRepoLineTracker.Integration/  ← ASP.NET host + Testcontainers Azurite
  PoRepoLineTracker.E2EAPI/       ← HTTP contract tests
  PoRepoLineTracker.E2EUI/        ← Playwright (mobile + desktop)

infra/                            ← Bicep: main.bicep, resources.bicep, availability-test.bicep, keyvault-access.bicep, storage-role.bicep
SCRIPTS/                          ← setup.ps1 (provision), verify-deploy.ps1 (post-deploy smoke)
.github/workflows/deploy.yml      ← lint + build → package → webapp deploy; manual `deploy_infra` input applies Bicep
```

## 6. Code-style & conventions

- **`AGENTS.md` + `.github/copilot-instructions.md`** are the writing ruleset (YAGNI ladder; never cut validation, error handling, security, a11y). Comments explain *why*, not *what* — match the tone of the inline rationale already in this repo.
- **Vertical Slice Architecture** under `Features/{FeatureName}/` — endpoints, validators, MediatR handlers, and queries co-located. Slices **may not reference each other**. Shared code that more than one slice needs goes to `Storage/`, `Analysis/`, `Services/`, or `Shared/Domain/`.
- **Source-generated JSON** in `Shared/Serialization/AppJsonSerializerContext`. The reflection resolver is deliberately unreachable from the client — every wire type needs a `[JsonSerializable]` entry, otherwise trim kills it.
- **No single-implementation interfaces** — only abstract when a test or alternate impl already proves the seam; otherwise use the concrete type.
- **AuthZ on repositories** — any route that takes a repository id from the URL uses `Auth/RepositoryOwnership`. The check lives outside slices so the second slice to need it cannot copy a stale copy.
- **Scoped CSS** — root the `.razor.css` at a plain `<div>`; Radzen components don't carry the `[b-xxx]` attribute. `::deep` for anything Radzen renders. When markup moves between components, its scoped CSS moves with it.
- **Headers/landmarks** — every page owns an `<h1>`, a `<PageTitle>`, and lives inside `<main>` in the layout. The page title is a real `<h1 class="page-hero__title">` written in the page's own markup (`PageHero` can't change the tag of a fragment handed to it).
- **Skip link** moves focus in code (`ElementReference.FocusAsync` on click with `preventDefault`), not via fragment navigation — Blazor's router intercepts anchor clicks.
- **Install button** — driven by the `beforeinstallprompt` event, captured in `index.html` (not in a module imported later). `registerInstallListener` returns the current availability *and* subscribes.
- **Config keys** go in `ConfigKeys` (Shared) — no magic strings.
- **Comments** explain the bug they prevent (the codebase already follows this convention; match it).

## 7. Testing strategy

- **Single stack** — xunit + NSubstitute + FluentAssertions. No other assertion or mocking library.
- **Coverage** — coverlet with `tests/coverlet.runsettings` on the Unit tier; CI does not currently enforce a threshold.
- **Dev/test auth** — `X-Fake-User` header (GUID verbatim; any other string hashes to a stable GUID). `FakeAuthHandler.ThrowIfProduction` makes registering it in Production a startup crash.
- **Seeding chart data** — `POST /api/dev/seed/repository` writes synthetic history (Development only, idempotent). Without it every chart test in E2EUI skips and the suite reports false health.
- **InternalsVisibleTo** — `PoRepoLineTracker.Unit` and `PoRepoLineTracker.Integration` see internals; prefer an `internal` helper over reflection.
- **E2EUI first run** — `pwsh tests/PoRepoLineTracker.E2EUI/bin/Debug/net10.0/playwright.ps1 install`.
- **Unit tests are not enough before commit** — they pass against changes that break the integration tier (composition-root changes in particular are invisible at unit scope and load-bearing for integration). Run Unit + Integration at minimum.

## 8. Boundaries

### Always (no further approval)

- **Security** — HTTPS-only cookies outside Development; `__Host-` antiforgery cookie gated by `Security:RequireSecureCookies`. `SecurityHeadersMiddleware` is non-negotiable. `ApiNotFoundMiddleware` answers 404 for unmatched `/api` GETs (middleware, not a route — a catch-all `/api/{**rest}` would win precedence against real routes and break authorization).
- **Auth in Production** — `ProductionAuthEnforcementMiddleware` redirects unauthenticated requests to the app's own `/login` (same origin, PWA `start_url: "/"`) and is unit-tested against a substituted `IWebHostEnvironment` (the only place its branch runs).
- **Validation at trust boundaries** — FluentValidation on every write slice; `Authorization` and `RepositoryOwnership` checked before any storage call.
- **Trim-safe JSON** — every wire type has a `[JsonSerializable]` entry; the reflection resolver is unreachable from the client.
- **Single source of truth** for "Total Lines" (`Shared/Domain/RepositoryTotals`), commit streaks (`Shared/Domain/CommitStreaks`), extension snapshots (`RepositoryTotals.LinesByFileTypeAsOf`), and progress frames (`Shared/Models/AnalysisProgressDto`). Three pages can show a streak; one home avoids three numbers that drift.
- **Progress frames are immutable in transit** — the loop hands the hub a fresh list each tick; appending to a shared list mid-serialize throws.

### Ask first

- New identity provider (Microsoft/Entra was removed because a Microsoft principal carries no GitHub credential — reintroducing it is a different conversation).
- New external SaaS, any AI/ML model API, or new analytics backend.
- Any new persistent table, new Bicep resource, or new App Insights query.
- Changes to `appsettings.json` schema (key renames break deployed instances).
- `dotnet format` / formatter version bumps.
- New test framework or assertion library (the stack is xunit + NSubstitute + FluentAssertions — period).

### Never

- **Microsoft/Entra sign-in, WebGL backdrop, Web Audio feedback, commit tagging, single-add `POST /api/repositories`, SmartAlerts, Failed Operations, AI model selector** — each was removed for a stated reason (see "Deliberately removed" in `AGENTS.MD`'s sibling notes; if reinstated, treat it as a new feature with its own SPEC delta).
- **AI/ML analytics** — sentiment, AI-share detection, ML on commits. Out of scope unless explicitly added.
- **Catch-all endpoint under `/api`** — `MapFallback("/api/{**rest}", ...)` was tried and reverted (caught real routes, passed authorization anonymously, came back 400 from the antiforgery gate).
- **Per-item storage lookup inside a loop** — `CommitExistsAsync` was removed for this; pre-load the set the loop needs (`GetCommitLineCountsByRepositoryIdAsync`) and look SHAs up in memory.
- **Mock-data banner / live mock mode** — retired. Data is real or absent.
- **Reflection resolver reachable from the trimmed client** — defeats the source-generated JSON contract.
- **`__Host-` cookie without `Secure`** — browsers reject it; the integration tier runs over plain HTTP as `"Test"` and opts out via `Security:RequireSecureCookies=false`.

## 9. Out of scope (explicit non-goals)

1. Microsoft/Entra sign-in (no GitHub credential → can't read repos).
2. WebGL backdrop + Web Audio feedback (UI; removed in cleanup).
3. Commit tagging (`CommitTaggerService`, the badge row it fed).
4. Single-add `POST /api/repositories` — `/bulk` is the only write path.
5. SmartAlerts, Failed Operations, AI model selector (removed; reasons documented).
6. AI/ML analytics (sentiment, AI-share, ML on commits).
7. Multi-tenant / org-level portfolios (single-user model; the user's repositories only).
8. Per-repo auto-prune TTL / auto-prune of aggregates (retention is "forever, manual purge" per agreement).
9. Custom code health weights or scoring changes (the bands are pinned: 6 line-oriented factors; markup band separate; nesting = 90th-percentile line depth; weights frozen).

## 10. Edge cases (load-bearing)

- **`TotalLines` on a commit is a snapshot, not a delta.** Repository size = newest commit's value, never summed, never windowed. `RepositoryTotals` is the single definition; two pages each grew their own version and printed different numbers under the same label.
- **Vendored third-party code is excluded, and one rule is by CONTENT not name.** `FileIgnoreFilter.ShouldIgnoreDirectory(path, entryNames)` prunes a directory whose immediate children include two or more root markers (`LICENSE*`, `.github`, `.gitmodules`, `CODEOWNERS`, `CODE_OF_CONDUCT.md`, `.pre-commit-config.yaml`, …) — whole other project copied in, whatever folder name. Filter changes only affect new analysis; stored counts need a re-analyse.
- **Comment syntax is per language family, and CSS is not C.** Plain `.css` has no `//` line comment (only `/* */`); it was registered with `//`, truncating `url(https://…)`. `.scss`/`.less` *do* have `//` — they cannot share an entry with `.css`. `.razor`/`.cshtml` use `@* *@` and `<!-- -->`. All registrations live in one table (`CommentSyntax`) read by both `SourceLineCounter` and `CodeMetricsAnalyzer` — two copies would drift.
- **Nesting is the 90th-percentile line depth, not the maximum.** One wrapped argument list measures far past the structure around it; using max let a 40-line file with a wrapped CSS string outrank genuinely tangled ones.
- **Markup is judged on its own nesting band.** Nested components aren't a smell; scoring `.razor` against the code band gave this repo's markup 48 against C# 94 — a fact about the format, not the code.
- **Code health is proxies, not a parser** — say so wherever it is shown. `CodeHealthCard` states this on the card, not in a tooltip. Measuring and judging are separate types (`CodeMetricsAnalyzer` counts; `CodeHealthScoring` judges).
- **Contributor share is weighted by lines added**, not averaged per commit. (A one-line commit and a 2,000-line refactor are not the same event.)
- **Analysis progress has one path.** The SignalR hub pushes frames; the fallback poll exists *only* for hub-unreachable and synthesises the same frames rather than handling completion itself. Do not add a second completion path — there used to be three and they had drifted.
- **The digest's read and its "mark seen" write are separate calls.** `GET /api/insights/digest` never records the visit; `POST /api/insights/digest/seen` does, and the banner only calls it once it has actually rendered. Merging them makes a page opened and closed without looking consume the window. The write is read-modify-write because `SavePreferencesAsync` upserts with `TableUpdateMode.Replace` — a partial preferences payload would blank the counted-extensions list.
- **A last visit is only used when it is between 12 hours and 90 days old.** More recent → trailing 7 days. Older → trailing 7 days too. Reporting a year under "since you were last here" is the recap's job.
- **The recap's windows are calendar edges.** `/api/recap/{year}` is 1 Jan – 31 Dec, fixed. Peak hour, weekday rhythm, language drift (in **share**), biggest single commit — figures that appear nowhere else.
- **Language drift is measured in share, not lines.** A file type's share can grow while the type shrinks — everything else shrank faster. A line-count delta cannot show it.
- **Cookie hardening is keyed on HTTPS, not the environment name.** `Security:RequireSecureCookies` decides `__Host-` + `Secure` for the antiforgery cookie. Integration runs as `"Test"` over plain HTTP and opts out explicitly.
- **Registration-time config cannot come from `ConfigureAppConfiguration`.** Those delegates run *after* top-level statements in `Program.cs`; anything read during `AddInfrastructure(builder.Configuration, …)` won't see them. `CustomWebApplicationFactory` uses `builder.UseSetting(...)` for exactly this.
- **A running app locks the build** — `dotnet build` fails with MSB3027. Stop the host first: `Get-Process -Name PoRepoLineTracker.API | Stop-Process -Force`.
- **Tree memoization** — `GitHubService` caches per-tree/per-blob counts for the lifetime of its scoped instance (one analysis run). Keyed on object id **and path** (filter answers depend on location). Dropped when the counted-extension set changes. `CountLinesInCommitAsync` returns a **copy** — a caller mutating a memoised instance would poison every later commit sharing that tree.
- **`SanitizeRepoName` keeps backslashes on Linux** — where this app deploys. The fix is in the helper, not in callers.
- **`libgit2` SIGABRT** under the App Service Linux container is reproducible; the runtime calls `git` CLI, not `LibGit2Sharp`, for clone/pull.

## 11. Error states

- **Unauthenticated `/api` request** → `ApiNotFoundMiddleware` answers 404 for unmatched GETs first; matched authenticated endpoints pass through `ProductionAuthEnforcementMiddleware`. In Production, unmatched authenticated requests on protected routes redirect to `/login`. In Development, `FakeAuthHandler` lets `X-Fake-User` through.
- **Antiforgery missing or wrong** on a write → 400 (after `UseAuthorization`, so unauthenticated writes still get 401).
- **Unmatched `/api` GET** → 404 (middleware, not the SPA fallback — the fallback used to return 200 carrying HTML to JSON callers).
- **Telemetry unavailable** (App Insights connection string absent) → app runs fine; `/diag` reports App Insights as "Not configured" (accurate, not a bug). `TelemetrySettings` is the single resolver both `AddTelemetry` and `/diag` use, so the page cannot disagree with what is actually registered.
- **OTLP endpoint absent** → spans built and dropped; `/diag` reports "OTLP Exporter — Not configured".
- **Repository id mismatch** between caller and stored record → 403 via `Auth/RepositoryOwnership`; slices never copy the check.
- **Stale `appsettings.Development.json`** without a `KeyVault:Uri` or `ApplicationInsights:ConnectionString` → app boots with secrets from user-secrets/env/local-override; `/diag` reflects what's actually registered.
- **First E2EUI run on a new machine** → Playwright browsers missing; `playwright.ps1 install` once per repo checkout.

## 12. Success criteria (numbered, measurable)

1. **Unit tier green on every commit.** `dotnet test tests/PoRepoLineTracker.Unit` returns 0.
2. **E2EUI tier zero skips.** A skip means the app is not up — not a fixture gap. `dotnet test tests/PoRepoLineTracker.E2EUI` exits 0 with no `Skip` entries.
3. **Bicep deploy is idempotent.** Re-applying `infra/main.bicep` against the existing RG succeeds; `RoleAssignmentExists` is tolerated.
4. **`/health` returns 200 with no auth** when the host is up (deploy smoke test + Azure probe).
5. **`/diag` is honest about telemetry** — App Insights and OTLP statuses match what `AddTelemetry` actually registered (single resolver). No "Not configured" claim that disagrees with registration.
6. **Analysis completes end-to-end on a real GitHub repository** with a known seed (e.g. a 30-day synthetic history from `/api/dev/seed/repository`) without skipping the per-commit count step.
7. **No Critical or Important `/security-review` or `/code-review` findings** at every release commit.
8. **Mock fallbacks work without keys** — app boots without `GitHub:ClientId`, `KeyVault:Uri`, or `ApplicationInsights:ConnectionString`; `/diag` reflects absence, doesn't crash.
9. **`AGENTS.md` + `.github/copilot-instructions.md` carry v4.10.0 ruleset** verbatim (validated by `ponytail`'s `check-rule-copies.js` equivalent — the two files share the same ruleset text).
10. **Repository ownership check is single-sourced** — `Auth/RepositoryOwnership` is the only place that decides "is this repo the caller's"; a grep for `RepositoryId` in `Features/` finds it referenced but never re-implemented.
11. **Coverage gate is informational, not a hard rule** (per agreement — the bar is "zero skips, no Critical/Important findings" not "X% line coverage").

## 13. Open questions

1. Should the recap gain a "compare to previous year" view? Currently it's a single year only — calendar edges are deliberate (see §10) but a delta view is a clean extension.
2. ~~Is there a desire for an offline read mode (PWA cached shell + cached API responses)?~~ **Resolved 2026-09-20**: shipped via `wwwroot/service-worker.published.js` (cached shell, server-route prefix exceptions) and `Components/Shared/OfflineIndicator.razor` (online/offline badge wired through `index.html`'s `registerNetworkStatusListener`).
3. Should the `POST /api/dev/seed/repository` endpoint move under an env flag (e.g. `Hosting:EnableDevSeed`) instead of relying on `IsDevelopment()` — for cases where a non-Development environment needs synthetic data (e.g. a staging slot)?
4. Code health weights are frozen. If a band needs tuning later, the change must be additive (a new factor) — not a re-weight of existing ones, or stored scores become incomparable. **Note 2026-09-20**: a sixth visualisation, `Components/Repositories/CodeHealthRadar.razor`, was added; weights are unchanged.
5. MinVer is at 6.0.0; bumping to 7.x is on the table for .NET 10 SDK compat but is a one-line change with no current need.
6. **New 2026-09-20** — Should the GitHub webhook receiver at `/api/webhooks/github` gain a per-repo allowlist or rate-limit? Currently any push to a tracked repo's default branch re-queues analysis. For a 500-repo portfolio, a flurry of pushes would saturate the analyzer.
7. **New 2026-09-20** — The portfolio export currently lives only at `/api/repositories/export`. Should the client expose a one-click "Export" affordance in `Repositories.razor`'s danger zone or hero, or keep it as a discoverable endpoint for tools?

---

## Migration (added 2026-09-19)

- **Ponytail v4.10.0** installed at repo root (`AGENTS.md`) and `.github/copilot-instructions.md`. Both are verbatim copies from `DietrichGebert/ponytail` and carry the cross-agent YAGNI/reuse/stdlib/platform/dep/one-liner/minimum ruleset.
- **`CLAUDE.md`, `NET_RULES.md`, `.claude/settings.json` retired.** Po platform conventions → ponytail's ruleset. "Things that will bite you" / domain rules → §6, §8, §10 of this SPEC.
- **`SPEC.md` is the new durable reference** for contracts, journeys, boundaries, success criteria. `AGENTS.md` is the writing ruleset. Both must be kept in sync — when a decision in §8/§10 changes, the matching rule here updates; when ponytail upgrades, the rule files sync verbatim.

---

## Delta: features landed 2026-09-20 (commits `8a439ed` + `307c20b`)

These features landed on `origin/master` after this SPEC's initial freeze. They are now **in-scope**; the non-goals in §9 are unchanged (AI/ML, SmartAlerts, Microsoft/Entra, etc. remain out). Each ships with its own unit tests; the unit tier is green.

### D.1 — In-scope features added

1. **GitHub webhook receiver** (`/api/webhooks/github`, `Features/Webhooks/GitHubWebhookEndpoints.cs`)
   - `POST /api/webhooks/github` accepts a GitHub `push` payload, verifies HMAC-SHA256 against `GitHub:WebhookSecret` (constant-time comparison via `CryptographicOperations.FixedTimeEquals`), and on a default-branch push for a tracked repository re-queues `AnalyzeRepositoryCommitsCommand` on a background task.
   - `AllowAnonymous` + `SkipAntiforgeryAttribute` because GitHub authenticates with the signature header, not a browser antiforgery cookie.
   - **Contract**: signature mismatches return `401`; malformed JSON `400`; untracked-repo `200` with `"Repository is not tracked"`; non-default-branch `200` with `"Ignored push to non-default branch {ref}"`; tracked default-branch push `202 Accepted` with `"Analysis queued"`.
   - **Risk**: any push for any tracked repo re-queues analysis — no throttle, no per-repo allowlist. See Open Question §13.6.

2. **Punchcard / activity-rhythm chart** (`/api/repositories/{id}/punchcard`, `Features/Repositories/GetRepositoryPunchcardQuery.cs`; `Components/Repositories/CommitActivityHeatmap.razor`)
   - 7×24 grid of `(dayOfWeek, hour)` buckets. `PunchcardItemDto` carries `CommitCount` + `LinesAdded` + `LinesRemoved`. Default range is 365 days; `Days` parameter is the knob.
   - Pure CSS-grid SVG-less heatmap — no chart library dependency added.
   - Rendered on `RepositoryDetail.razor`; `EmptyHint` points the user at the range selector.

3. **Code health radar chart** (`Components/Repositories/CodeHealthRadar.razor`)
   - Pure-SVG 6-axis radar visualising the existing six line-oriented code-health factors (SPEC §10). **Scoring weights unchanged** — radar is a visualisation, not a re-weighting. Markup confirms this.

4. **PWA offline indicator** (`Components/Shared/OfflineIndicator.razor` + `wwwroot/service-worker.published.js` + `wwwroot/index.html`)
   - The published service worker caches the Blazor shell; the dev worker remains a no-op (SPEC §10 "the swap happens via `<ServiceWorker>` item in the client csproj"). Server-route prefix exceptions (`/api/`, `/auth/`, `/hubs/`, `/health`, `/signin-`, `/signout-`) stay correct.
   - The `OfflineIndicator` calls `registerNetworkStatusListener` (captured at boot in `index.html`, not in a module imported later, per SPEC §6).
   - **The "mock-data banner" remains retired.** Offline is a different concept — the indicator shows network status, not data provenance. SPEC §8 explicit "never" stays.

5. **Portfolio export** (`/api/repositories/export`, `Features/Repositories/PortfolioExportEndpoints.cs`)
   - `GET /api/repositories/export` returns JSON; `?format=csv` returns `text/csv` with header `Owner,Repository,TotalLines,LastAnalyzedUtc,CloneUrl`. CSV writer lives next to the endpoint and is exported (not private) so the `PortfolioExportTests` suite can pin it.
   - Uses `RepositoryTotals.LatestTotalLines` (SPEC §10 single source of truth) for the totals.

6. **Custom glob support for counted extensions** (`Analysis/FileIgnoreFilter.cs`, `Storage/UserPreferencesEntity.cs`, `Models/UserPreferences.cs` in Shared)
   - The user-preferences schema now stores counted files as globs (e.g. `*.cs`, `**/*.razor`) in addition to literal extensions. `FileIgnoreFilter` accepts both shapes.

7. **RepositoriesGrid extraction** (`Components/Repositories/RepositoriesGrid.razor`, `Models/RepositoryGridRow.cs`, slim `Pages/Repositories.razor`)
   - The bespoke table lifted out of the 1,200-line `Repositories.razor` into a reusable grid component. Five columns: Repository / Total Lines / Last Analyzed / Status / Actions. CSS moved into `RepositoriesGrid.razor.css`. Phase 3 pick **A7** (tiles-above-grid, single visual seam) preserved.

### D.2 — SPEC sections updated to acknowledge the delta

- **§2 User Journeys** — Add: "Webhook-driven auto re-analysis" (server-side, not a user journey per se but a primary trigger for the read journey); "Read the punchcard" and "Read the code health radar" on `RepositoryDetail`; "Export the portfolio" as a discoverable read-side action.
- **§5 Project Structure** — Add `Features/Webhooks/`, `Components/Repositories/CommitActivityHeatmap.razor`, `Components/Repositories/CodeHealthRadar.razor`, `Components/Shared/OfflineIndicator.razor`. Add `Models/Dtos/GitHubWebhookPayload.cs`, `PortfolioExportRowDto.cs`, `PunchcardItemDto.cs`.
- **§6 Code-style** — Note: `JsonSerializerContext` registration of the three new DTOs is the source of truth for trim (SPEC §8). Commit the `RepositoryGridRow` lift to `Models/` was a no-architecture-change refactor.
- **§8 Boundaries — Always** — Add: webhook signature verification uses `CryptographicOperations.FixedTimeEquals`. Add: `JsonSerializerContext` registration for every wire type that crosses the trim boundary (the three new DTOs are registered in `AppJsonSerializerContext.cs`).
- **§8 Boundaries — Ask first** — Add: changes to the webhook allowlist / rate-limit posture. Add: changes to the radar/heatmap visuals.
- **§8 Boundaries — Never** — Unchanged. The mock-data banner stays retired. Offline indicator ≠ mock-data banner (see §D.1 #4).
- **§9 Out of scope** — Unchanged. All seven new features are in-scope per §D.1. AI/ML, Microsoft/Entra, SmartAlerts, etc. are still out.
- **§11 Error states** — Add: webhook 401 (signature), 400 (malformed), 200-ignored (non-default branch / untracked repo), 202 (queued). Add: export 401 (unauthenticated).
- **§12 Success criteria** — Add:
  12. **Webhook round-trips end-to-end.** A signed push payload to `/api/webhooks/github` against a tracked repo returns `202 Accepted` and re-queues analysis; a tampered signature returns `401` and queues nothing.
  13. **Portfolio export serves both formats.** `/api/repositories/export` returns JSON for the default and `text/csv` for `?format=csv`; both pin `RepositoryTotals.LatestTotalLines` as the totals source.
  14. **PWA offline indicator survives a network drop.** A browser session that goes offline renders the indicator within the next paint cycle and recovers when the network returns.
