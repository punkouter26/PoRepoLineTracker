# tasks/todo.md — PoRepoLineTracker

Vertical-slice checklist, ≤5 files per task, one commit per task. Read `tasks/plan.md` for context; read `SPEC.md` for contracts and boundaries; follow `AGENTS.md` + `.github/copilot-instructions.md` for the writing ruleset.

**Hard gate:** No task is started before the previous task's commit lands and its verification command passes. No commit before the verification command for that task passes.

---

## Stream A — Telemetry & health single source of truth

### A1. Bounded `Channel<AnalysisProgressDto>` between analysis loop and SignalR hub

- **Acceptance**
  - `AnalysisHub` reads from a static `Channel.CreateBounded<AnalysisProgressDto>` with capacity 64 and `FullMode = BoundedChannelFullMode.DropOldest`, `SingleReader = true`.
  - `GitHubService.PublishAsync` writes to the channel instead of calling `Hub.Clients.All.SendAsync` directly.
  - When the channel is full, the oldest frame is dropped (corner-cut; SPEC §10 accepts this).
  - Unit test: "200 frames of writes result in at most 64 frames read; the most recent frame is the one surviving under sustained load".
  - `ponytail:` comment on `DropOldest` naming the corner-cut and upgrade path (switch to `Wait` if loss becomes observable).
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.API/Hubs/AnalysisHub.cs`
  - `src/PoRepoLineTracker.API/Analysis/GitHubService.cs`
  - `src/PoRepoLineTracker.API/Features/Repositories/AnalyzeRepositoryCommits.cs`
  - `src/PoRepoLineTracker.API/Platform/ConfigKeys.cs`
  - `tests/PoRepoLineTracker.Unit/Hubs/AnalysisHubChannelTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.Unit --filter "FullyQualifiedName~AnalysisHubChannelTests"`
  - Full Unit tier: `dotnet test tests/PoRepoLineTracker.Unit`
- **Dependencies** — none.

### A2. Custom `Meter` counter for `LinesCounted` per repository

- **Acceptance**
  - `Telemetry/AnalysisMetrics.cs` (new) declares `Counter<long> LinesCounted` and `Histogram<double> CommitCountDurationMs` on a single static `Meter`.
  - `GitHubService` increments `LinesCounted.Add(rows, new KeyValuePair<string, object?>("repo", repoId))` after each commit is counted.
  - Registration goes through `TelemetryExtensions.AddTelemetry` (so the counter is observable in `/diag`).
  - Unit test: "after `AddTelemetry`, the `AnalysisMetrics.LinesCounted` counter is in the registered `MeterListener` set".
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.API/Telemetry/AnalysisMetrics.cs` (new)
  - `src/PoRepoLineTracker.API/Analysis/GitHubService.cs`
  - `src/PoRepoLineTracker.API/Telemetry/TelemetryExtensions.cs`
  - `src/PoRepoLineTracker.API/Features/Diagnostics/DiagnosticsEndpoints.cs`
  - `tests/PoRepoLineTracker.Unit/Telemetry/AnalysisMetricsTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.Unit --filter "FullyQualifiedName~AnalysisMetricsTests"`
  - Full Unit tier: `dotnet test tests/PoRepoLineTracker.Unit`
- **Dependencies** — A1 merged.

### A3. Per-dependency `HealthCheck` registrations

- **Acceptance**
  - `AzureTableStorageHealthCheck` (existing under `Storage/`) is wrapped as `IHealthCheck` and registered under name `azure-table-storage`.
  - `Features/Diagnostics/GitHubApiHealthCheck.cs` (new) returns `Healthy` when `X-RateLimit-Remaining > 100`, `Degraded` when `< 100`, `Unhealthy` on 401/403 (rate-limit aware; no GitHub credential stored).
  - `KeyVault` probe returns `Healthy` only when `KeyVault:Uri` is configured AND a credential probe succeeds; `Degraded` otherwise (the SPEC §11.3 contract: "Telemetry unavailable → app runs fine; `/diag` reports Not configured").
  - `/health` returns 200 with the per-check breakdown; `/diag` reads the same registrations.
  - Unit tests: one per check, asserting Healthy/Degraded/Unhealthy for the three inputs.
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.API/Storage/AzureTableStorageHealthCheck.cs`
  - `src/PoRepoLineTracker.API/Features/Diagnostics/GitHubApiHealthCheck.cs` (new)
  - `src/PoRepoLineTracker.API/Extensions/HealthChecksExtensions.cs` (new)
  - `src/PoRepoLineTracker.API/Features/Diagnostics/DiagnosticsEndpoints.cs`
  - `tests/PoRepoLineTracker.Unit/Diagnostics/HealthCheckRegistrationTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.Unit --filter "FullyQualifiedName~HealthCheckRegistrationTests"`
  - `curl -sS https://localhost:5003/health | jq` → JSON with `status: Healthy` and three `entries`.
  - Full Unit tier: `dotnet test tests/PoRepoLineTracker.Unit`
- **Dependencies** — A2 merged.

### Checkpoint (A1–A3)

- Run `dotnet test tests/PoRepoLineTracker.Integration` with Azurite up. Expected: zero skips; `/diag` assertions for `AnalysisMetrics` and the new health checks pass.
- `curl https://localhost:5003/diag -H "X-Fake-User: 00000000-0000-0000-0000-000000000001" | jq '.telemetry.analysis' ` returns the registered counter.

---

## Stream B — UI scale prep

### B1. `RadzenDataGrid` template replacing the bespoke table on `Repositories.razor` (Phase 3 pick: A7)

- **Acceptance**
  - New `Components/Repositories/RepositoriesGrid.razor` renders a `RadzenDataGrid` with columns `FullName`, `TotalLines`, `Health`, `Last commit`.
  - `Pages/Repositories.razor` keeps `PortfolioStatTiles` above the grid (Phase 3 pick A7: tiled header + grid, single visual seam). The bespoke table is replaced with `<RepositoriesGrid Items="@repositories" />`.
  - `Repositories.razor.css` selectors that target the old table move to `RepositoriesGrid.razor.css`. No inline CSS.
  - Page hero carries h1 + subtitle "Your repositories — N total · M lines." (Phase 3 pick B1). Subtitle values are server-known so `[StreamRendering]` (B3) renders the same text pre- and post-hydration.
  - `AccessibilityUiTests` passes unchanged: one h1 on `/repositories`, distinct page title, sortable columns announced to assistive tech.
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.Client/Components/Repositories/RepositoriesGrid.razor` (new)
  - `src/PoRepoLineTracker.Client/Components/Repositories/RepositoriesGrid.razor.css` (new)
  - `src/PoRepoLineTracker.Client/Pages/Repositories.razor`
  - `src/PoRepoLineTracker.Client/Components/Repositories/PortfolioStatTiles.razor`
  - (no test file added in B1 — verification is by `AccessibilityUiTests` after B2)
- **Verification**
  - `dotnet build src/PoRepoLineTracker.Client` succeeds with no trim warnings introduced.
  - `dotnet test tests/PoRepoLineTracker.Unit` still green (no behaviour change in B1).
- **Dependencies** — A3 merged.

### B2. `AllowVirtualization="true"` + seeded fixture for the long-list assertion

- **Acceptance**
  - `RepositoriesGrid.razor` sets `AllowVirtualization="true"` and `PageSize="20"`.
  - `E2ESeeder.cs` grows from N=1 to N=200 entries (idempotent; uses `/api/dev/seed/repository`).
  - New `RepositoriesGridUiTests.cs` (in E2EUI) scrolls the grid to row 200 and asserts it renders.
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.Client/Components/Repositories/RepositoriesGrid.razor`
  - `tests/PoRepoLineTracker.E2EUI/E2ESeeder.cs`
  - `tests/PoRepoLineTracker.E2EUI/RepositoriesGridUiTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.E2EUI --filter "FullyQualifiedName~RepositoriesGridUiTests"`
  - Full E2EUI: `dotnet test tests/PoRepoLineTracker.E2EUI` — **zero skips**.
- **Dependencies** — B1 merged.

### B3. `[StreamRendering]` on Repositories, Insights, RepositoryDetail (Phase 3 hero: B1 + B10)

- **Acceptance**
  - The three pages carry `@attribute [StreamRendering]`.
  - `Pages/Repositories.razor` keeps B1 hero (h1 + subtitle "Your repositories — N total · M lines."); subtitle values are server-known (counts from `GetAllRepositoriesQuery`) so the prerendered text matches post-hydration exactly.
  - `Pages/Insights.razor` and `Pages/RepositoryDetail.razor` carry the B10 minimal hero (h1 only, no subtitle) — Phase 3 decision: minimal is correct where there's no count or orientation affordance to surface.
  - New `PreRenderUiTests.cs` (E2EUI) asserts: "first request to `/repositories` while authenticated returns a non-empty `<main>` before client-side hydration".
  - Logout flow assertion: after `SignOut`, the prerendered snapshot matches `/auth/me`'s `isAuthenticated: false` (no stale auth UI from a previous user).
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.Client/Pages/Repositories.razor`
  - `src/PoRepoLineTracker.Client/Pages/Insights.razor`
  - `src/PoRepoLineTracker.Client/Pages/RepositoryDetail.razor`
  - `tests/PoRepoLineTracker.E2EUI/PreRenderUiTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.E2EUI --filter "FullyQualifiedName~PreRenderUiTests"`
- **Dependencies** — B2 merged.

### B4. `IAnalysisFeed` typed contract in `Shared/Hubs/`

- **Acceptance**
  - `Shared/Hubs/IAnalysisFeed.cs` (new) declares `Task Frame(AnalysisProgressDto frame)` and `Task Complete(AnalysisCompleteDto complete)`.
  - `Client/Services/AnalysisFeedClient.cs` uses the typed hub contract (generator output stays under `TrimmerRoots.xml`).
  - New unit test asserts: "after `IAnalysisFeed.Frame` is invoked via the typed contract, the `AnalysisProgressDto` payload matches the source-generated shape".
- **Files** (`≤5`)
  - `src/PoRepoLineTracker.Shared/Hubs/IAnalysisFeed.cs` (new)
  - `src/PoRepoLineTracker.Client/Services/AnalysisFeedClient.cs`
  - `src/PoRepoLineTracker.Client/TrimmerRoots.xml`
  - `tests/PoRepoLineTracker.Unit/Hubs/IAnalysisFeedContractTests.cs` (new)
- **Verification**
  - `dotnet test tests/PoRepoLineTracker.Unit --filter "FullyQualifiedName~IAnalysisFeedContractTests"`
  - `dotnet build src/PoRepoLineTracker.Client` — no new trim warnings.
- **Dependencies** — B3 merged.

---

## Final checkpoint (Phase 4 → Phase 5)

- All 7 commits land on `master` in order: `A1`, `A2`, `A3`, `B1`, `B2`, `B3`, `B4`.
- Full Unit tier: `dotnet test tests/PoRepoLineTracker.Unit` — 0 failures.
- Full E2EUI: `dotnet test tests/PoRepoLineTracker.E2EUI` — **0 skips**.
- E2EAPI smoke: `dotnet test tests/PoRepoLineTracker.E2EAPI` — 0 failures, 0 skips.
- Integration smoke: `dotnet test tests/PoRepoLineTracker.Integration` — 0 failures, 0 skips.
- Capture output for SPEC §12 success criteria 1, 2, 4, 5 — Phase 5 will paste them into the recap.
