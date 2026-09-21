# tasks/plan.md — PoRepoLineTracker

Phase 2 plan. Whole-app freeze lives in `../SPEC.md`; this file is the architecture, dependency graph, and risk table for the next two streams of work. **Read-only at this stage.** No code is written until the gate is opened by your explicit `approved` on `tasks/todo.md`.

---

## A. Decisions locked by SPEC (no re-decision here)

- .NET 10, Blazor WASM trimmed, OpenTelemetry 1.15.3, Radzen 8.4.2, MediatR 14.1.0, FluentValidation 12.0.0, HybridCache 10.0.0, Azure.Data.Tables 12.11.0, xunit + NSubstitute + FluentAssertions + Playwright. Source-generated JSON; reflection resolver unreachable from the client.
- Vertical Slice Architecture; slices may not reference each other. Shared code lives in `Storage/`, `Analysis/`, `Services/`, `Shared/Domain/`, `Shared/Models/`. Repository ownership check is single-sourced in `Auth/RepositoryOwnership`.
- CI gate is Unit-only (commit `845c024`). Integration/E2E run locally / on demand. E2EUI zero-skips invariant.
- Out-of-scope (SPEC §9): Microsoft/Entra, WebGL/Audio, commit tagging, single-add, SmartAlerts, Failed Operations, AI model selector, AI/ML, multi-tenant, auto-prune TTL, custom code-health weights.
- `TelemetrySettings` is the single resolver shared by `AddTelemetry` and `/diag`; the page cannot disagree with what is actually registered.

## B. Streams

Two parallel work streams, both inside the SPEC's boundaries. They are sequenced because Stream B's first task consumes a small refactor produced by Stream A's last task.

### Stream A — Telemetry & health single source of truth

Aligns with SPEC §12.5 ("`/diag` is honest about telemetry") and §12.4 ("`/health` returns 200 with no auth"). Reuses the existing `TelemetrySettings` resolver; no new packages.

| # | Title | Files (≤5) | Why |
|---|---|---|---|
| A1 | Bounded `Channel<AnalysisProgressDto>` between analysis loop and SignalR hub | `Hubs/AnalysisHub.cs`, `Analysis/GitHubService.cs`, `Features/Repositories/AnalyzeRepositoryCommits.cs`, `Platform/ConfigKeys.cs`, `tests/PoRepoLineTracker.Unit/Hubs/AnalysisHubChannelTests.cs` | Backpressure on slow consumers without a new dependency. Replaces fire-and-forget publish. |
| A2 | Custom `Meter` counter for `LinesCounted` per repository | `Telemetry/AnalysisMetrics.cs` (new), `Analysis/GitHubService.cs`, `Telemetry/TelemetryExtensions.cs`, `tests/PoRepoLineTracker.Unit/Telemetry/AnalysisMetricsTests.cs` | Real metric the user can read in Jaeger at <http://localhost:16686>; no new dependency. |
| A3 | Per-dependency `HealthCheck` registrations (Azure Table Storage, Key Vault probe, GitHub API rate-limit aware) | `Storage/AzureTableStorageHealthCheck.cs` (existing — wrap as `IHealthCheck`), `Features/Diagnostics/GitHubApiHealthCheck.cs` (new), `Extensions/HealthChecksExtensions.cs` (new), `Features/Diagnostics/DiagnosticsEndpoints.cs`, `tests/PoRepoLineTracker.Unit/Diagnostics/HealthCheckRegistrationTests.cs` | `/health` and `/diag` agree; no net-new code beyond the GitHub probe. |

**Dependencies between A tasks:** A1 → A2 → A3, sequential. A2 registers the meter with the existing `TelemetrySettings` resolver so the registration is observable in `/diag`. A3 wires the new checks into `/health`; `/diag` reads the same registrations.

### Stream B — UI scale prep

Aligns with SPEC §12.2 (E2EUI zero-skips — these changes are how a 200-row `Repositories` list exercises the virtualization assertion). Radzen-first, scoped CSS, no inline CSS.

| # | Title | Files (≤5) | Why |
|---|---|---|---|
| B1 | `RadzenDataGrid` template replacing the bespoke table on `Repositories.razor` | `Components/Repositories/RepositoriesGrid.razor` (new), `Components/Repositories/RepositoriesGrid.razor.css` (new), `Pages/Repositories.razor`, `Components/Repositories/PortfolioStatTiles.razor` | Same data, fewer lines, paging/virtualization handled by Radzen. |
| B2 | `AllowVirtualization="true"` on `RepositoriesGrid` and prep the seeded fixture so the E2EUI assertion is real | `Components/Repositories/RepositoriesGrid.razor`, `tests/PoRepoLineTracker.E2EUI/E2ESeeder.cs`, `tests/PoRepoLineTracker.E2EUI/RepositoriesGridUiTests.cs` (new) | Long-list scroll is invisible on the current fixture; the seeder makes the assertion observable. |
| B3 | `[StreamRendering]` on `Repositories` and `Insights` pages; document the per-page opt-in | `Pages/Repositories.razor`, `Pages/Insights.razor`, `Pages/RepositoryDetail.razor`, `tests/PoRepoLineTracker.E2EUI/PreRenderUiTests.cs` (new) | First-meaningful-paint improvement for the two pages the user lands on first. |
| B4 | `IAnalysisFeed` typed contract in `Shared/Hubs/` for testability through `InternalsVisibleTo` | `Shared/Hubs/IAnalysisFeed.cs` (new), `Client/Services/AnalysisFeedClient.cs`, `tests/PoRepoLineTracker.Unit/Hubs/IAnalysisFeedContractTests.cs` (new) | The hub is reached from both server and client; one typed contract surfaces drift. |

**Dependencies between B tasks:** B1 → B2 → B3 → B4, sequential. B2 depends on B1 because the grid is the new fixture subject. B4 depends on B3 because the typed contract reads from the same `StreamRendering` flow.

**Cross-stream dependency:** Stream A's A3 (the GitHub probe) is what Stream B's B4 typed contract needs to assert "hub connection succeeds when GitHub is healthy". Streams are sequenced A → B; **B does not start until A is merged**.

## C. Dependency graph

```
A1 ──► A2 ──► A3 ──┐
                    ▼
              B1 ──► B2 ──► B3 ──► B4
```

Each task lands as one commit (Phase 4 rule). No multi-task commits.

## D. Risk table

| Risk | Likelihood | Mitigation |
|---|---|---|
| `DropOldest` channel policy hides progress loss on slow consumers | Medium | SPEC §10 accepts this loss deliberately; reconnect re-syncs via the fallback poll. Add a unit test asserting "frame X survives 200 frames of writes" so the policy is pinned. |
| Custom `Meter` not picked up by `/diag` because `TelemetrySettings` is read at request time | Low | The resolver is the single source; A2 wires through `TelemetryExtensions.AddTelemetry` so registration is observable. Unit test: "after `AddTelemetry`, `AnalysisMetrics` counter is in the registry". |
| `IHealthCheck` for Azure Table Storage wraps an existing probe — drift between the two | Low | A3 deletes the existing `AzureTableStorageHealthCheck` and replaces it with the `IHealthCheck` impl; `/diag` switches its reader to the registry. |
| `RadzenDataGrid` migration breaks `AccessibilityUiTests` (column headers, focus order) | Medium | B1 ports `Repositories.razor.css` selectors 1:1 into `RepositoriesGrid.razor.css`. `AccessibilityUiTests` runs before and after B1 to assert zero regression; B1 is **not** merged if a regression appears. |
| `IAnalysisFeed` typed contract generator surfaces trim warnings (`IL2110`/`IL2111`) on the client | Low | The codebase already has `TrimmerRoots.xml`; B4's new types are explicitly added there. |
| Stream B blocks on E2EUI long-list fixture data | Low | B2 uses `/api/dev/seed/repository` (already idempotent) to write enough rows; no new endpoint. |
| `SecurityHeadersMiddleware` blocks the new health-check `AllowAnonymous` registration | Low | The existing `/health` is already `AllowAnonymous`; A3 only adds registrations under the same map. |
| `[StreamRendering]` shows a stale snapshot to authenticated users on logout | Low | B3 adds an E2EUI assertion: "after `SignOut`, the rendered snapshot matches `/auth/me`'s `isAuthenticated: false`". |

## E. Checkpoints (every 2–3 tasks)

- After **A2**: re-run the Unit tier end-to-end + spin Azurite + run Integration. Expected: zero skips, no regression in `/diag` or `/health` assertions.
- After **B1**: run E2EUI `AccessibilityUiTests`. Expected: one h1 per page, distinct titles, no regressions in column headers / focus order.
- After **B4**: run the full four-tier suite locally. Expected: E2EUI zero skips; E2EAPI all green; Integration green; Unit green.

A checkpoint is **a halt, not a release.** The Phase 5 review gates (`/code-review`, `/security-review`, `/simplify`) run only when all 7 tasks are merged.

## F. What this plan does NOT include (deferred)

- Recap comparison view (SPEC §13 open question #1).
- Offline read mode (SPEC §13 #2).
- `POST /api/dev/seed/repository` env-flag refactor (SPEC §13 #3).
- Code-health re-weight or new factor (SPEC §13 #4 — explicit freeze).
- MinVer 7.x bump (SPEC §13 #5).
- Snapshot testing with `Verify` (item 14 of the Phase 2 menu) — not in scope for these streams; can become a follow-up if the E2EAPI tier is unstable on DTO drift.
- `Spectre.Console` in `SCRIPTS/setup.ps1` (menu item 10) — script-only, no runtime impact, not blocking.
- `AspNetCore.HealthChecks.Uris` package add (menu item 12) — `GitHubApiHealthCheck` covers the only probe we need; the package is unnecessary overhead.
- `System.IO.Hashing.XxHash3` (menu item 9) — no current consumer; stays on the bench.

## G. Acceptance gates for Phase 4 → Phase 5

- All 7 tasks merged, each in its own commit, each behind a passing Unit tier.
- SPEC §12.1 (Unit green), §12.2 (E2EUI zero-skips), §12.5 (`/diag` honest) verified with concrete command output captured for Phase 5.
- `tasks/todo.md` is fully checked.

---

## H. Re-baseline delta (2026-09-20)

Between the original plan (`f0d93fc` + `b7e7c2c`) and now, two commits landed on `origin/master` in parallel with this session:

| Commit | Title | Impact |
|---|---|---|
| `8a439ed` | (no subject) | Created `Components/Repositories/RepositoriesGrid.razor` + `.razor.css`, `Models/RepositoryGridRow.cs`, slimmed `Pages/Repositories.razor`, added `RepositoryGridRowTests`. **B1's deliverable landed before B1 started.** |
| `307c20b` | `feat: implement features 3, 5, 6, 7, 9, 10 (PWA offline, webhooks, radar chart, punchcard, custom globs, portfolio export)` | 37 files, +1,553/-18. New surface outside the original SPEC's scope. |

**Stream A — fully landed (A1, A2, A3 committed):**
- A1 `2370a38` — bounded `Channel<AnalysisProgressDto>` between analysis loop and SignalR hub.
- A2 `fa9e6e7` — `AnalysisMetrics.LinesCounted` + `CommitCountDurationMs` meter counter + histogram.
- A3 `a1f975a` — three named `IHealthCheck` registrations; `/health` and `/diag` share the registry.

**Stream B — partially landed by parallel work:**
- B1 `c22be41` — `RepositoriesGrid.razor` extracted (my build/test passed; the parallel `8a439ed` had already shipped the bulk of it).
- B2–B4 — supersedable by the parallel work. `307c20b` shipped PWA offline + the radar + the punchcard + portfolio export + custom globs + the Repositories page additions; the remaining UI-scale items in B2-B4 are now micro-features (`AllowVirtualization`, `[StreamRendering]`, `IAnalysisFeed` typed contract).

**Decision (Phase 0/2 gate):** Stream A is complete. Stream B's B2-B4 as originally specified are deprecated. Replacing them with a single verification slice that runs against the current codebase:

| # | Title | Files (≤5) | Why |
|---|---|---|---|
| B' | Phase 5 prep — verify full tier + add critical-path unit tests for the 7 new features | `tests/PoRepoLineTracker.Unit/Features/Webhooks/GitHubWebhookTests.cs` (already shipped), `tests/PoRepoLineTracker.Unit/Features/Repositories/PortfolioExportTests.cs` (already shipped), `tests/PoRepoLineTracker.Unit/Components/Shared/OfflineIndicatorTests.cs` (new — bUnit-less test via static analysis is impractical; document why and pin via E2EUI in Phase 5), `tests/PoRepoLineTracker.Unit/Storage/PortfolioExportRowMappingTests.cs` (new), `tests/PoRepoLineTracker.Unit/Analysis/CustomGlobFilterTests.cs` (already shipped) | Pin the contract on every shipped feature so a Phase 5 `/code-review` finds drift, not behaviour. |

**Concretely, for Phase 5 acceptance:**
- All 5 test tiers run (Unit, Integration, E2EAPI, E2EUI, E2EUI smoke).
- `/code-review` runs against the current `master`.
- `/security-review` runs; the webhook signature verification is the highest-priority new surface.
- `/simplify` runs; the 1,553-line delta is the highest-value target.
