# CLAUDE.md

Guidance for Claude Code working in this repository.

`AGENT.MD` holds the long-form architecture rationale and `NET_RULES.md` the numbered rules the
codebase cites (you will see "Rule 4.2" in comments). Read those for *why*. This file is what you
need to not waste a cycle.

## What this is

Blazor WASM + ASP.NET Core (net10.0) that tracks lines of code across a user's GitHub
repositories over time: line-count history, per-extension composition, contributor stats, and a
heuristic AI-authorship score. Sign-in is **GitHub OAuth only**.

## Projects

```
PoRepoLineTracker.Shared   DTOs, domain models, strongly-typed IDs, validation. LEAF — no project refs.
PoRepoLineTracker.API      Minimal API + storage + feature slices. Also serves the WASM client.
PoRepoLineTracker.Client   Blazor WASM. Depends on Shared only; talks to the API over HTTP.
```

Vertical slices under `API/Features/{Name}/` own their endpoints, commands/queries and handlers
together. **Slices must not reference each other** — `GlobalUsings.cs` deliberately omits
`Features.*` so cross-slice coupling needs an explicit `using`, and only `Extensions/` (the
composition root) has one.

## Build, run, test

```bash
docker compose up -d                                   # Azurite (Table Storage emulator)
dotnet run --project src/PoRepoLineTracker.API --launch-profile https   # https://localhost:5001

dotnet build
dotnet test tests/PoRepoLineTracker.Unit          # 214 — no external deps
dotnet test tests/PoRepoLineTracker.Integration   # 76  — WebApplicationFactory + Testcontainers Azurite
dotnet test tests/PoRepoLineTracker.E2EAPI        # 62  — needs the app running
dotnet test tests/PoRepoLineTracker.E2EUI         # 64  — needs the app running + Playwright (~3m30s)
```

All four tiers are **green with zero skips**. Keep it that way — a skip in E2EUI now means the app
isn't up, not that a fixture is missing.

First E2EUI run needs browsers:
`pwsh tests/PoRepoLineTracker.E2EUI/bin/Debug/net10.0/playwright.ps1 install`

## Things that will bite you

**A running app locks the build.** `dotnet build` fails with MSB3027 "file is locked by
PoRepoLineTracker.API". Stop it first:
`Get-Process -Name PoRepoLineTracker.API | Stop-Process -Force`.

**Unit tests alone are not enough before committing.** They pass against changes that break the
whole integration tier. Composition-root changes in particular (`InfrastructureServiceExtensions`,
`AuthServiceExtensions`) are invisible to the unit tier and load-bearing for every integration
test. Run Unit + Integration at minimum.

**Registration-time config cannot come from `ConfigureAppConfiguration`.** Those delegates run
*after* `Program.cs` executes its top-level statements, so anything read during
`AddInfrastructure(builder.Configuration, ...)` will not see them. `CustomWebApplicationFactory`
uses `builder.UseSetting(...)` for exactly this. Values read lazily at request time are fine either
way, which is why only one key needs the earlier hook.

**Cookie hardening is keyed on HTTPS, not the environment name.**
`Security:RequireSecureCookies` (default: true outside Development) decides whether the antiforgery
cookie gets `__Host-` + `Secure`. A `__Host-` cookie without `Secure` is rejected by browsers, and
one *with* `Secure` never comes back over http — either way every state-changing request fails
while reads look fine. The integration tier runs as environment `"Test"` over plain HTTP and opts
out explicitly.

**Scoped CSS needs a plain element at the component root.** A `.razor.css` rule compiles to
`.foo[b-xxx]`, and nothing a Radzen component renders carries that attribute. Root at a plain
`<div>` (see `ChartCard`, `AnalysisStatusCell`) and use `::deep` for anything Radzen renders.
Rules that silently match nothing have shipped here more than once. **When you move markup between
components, move its scoped CSS with it** — otherwise it stays compiled against the old scope id.

**Writes need the antiforgery pair.** POST/PUT/DELETE under `/api` require both halves: the cookie
from `GET /api/antiforgery/token` and the same token echoed as `X-CSRF-TOKEN`. Missing either gives
400. `tests/PoRepoLineTracker.E2EUI/E2ESeeder.cs` is a worked example in ~40 lines.

## Test conventions

xunit + NSubstitute + FluentAssertions everywhere. No other assertion or mocking library.

**Dev/test auth is header-driven**: send `X-Fake-User` (a GUID verbatim, any other string hashed to
a stable GUID) and optionally `X-Fake-Roles`. `FakeAuthHandler.ThrowIfProduction` makes registering
it in Production a startup crash. There is no dev-login route.

**Seeding chart data**: `POST /api/dev/seed/repository` (Development only) writes synthetic commit
history so chart assertions have something to assert on. Idempotent. Without it every chart test in
E2EUI skips itself and the suite reports false health.

`InternalsVisibleTo` is already set for both test assemblies, so prefer making a helper `internal`
over testing it through reflection.

## Domain rules worth knowing

**`TotalLines` on a commit is a snapshot of the whole repository, not a delta.** So a repository's
current size is the value on its *newest* commit — never a sum across commits, and never windowed.
`RepositoryTotals` (Shared) is the single definition; call it rather than re-deriving. Two pages
each grew their own version and printed different numbers under the same label.

**AI share is weighted by lines added**, not averaged per commit — a one-line human commit and a
2,000-line generated file are not the same event.

**Analysis progress has one path.** The SignalR hub (`/hubs/analysis`) pushes frames; the fallback
poll in `Repositories.razor` exists only for when the hub is unreachable, and it *synthesises the
same frames* rather than handling completion itself. Do not add a second completion path — there
used to be three, and they had drifted.

**Saved preferences live in `UserPreferencesClient`**, which raises `Changed` after a write. Pages
read from it and subscribe; they do not render their own copy of a settings control.

## Conventions

- `TreatWarningsAsErrors=true`, `Nullable=enable`, `LangVersion=preview` — a warning fails the build.
- Trimming is ON for the WASM client. Every wire type needs a `[JsonSerializable]` entry in
  `AppJsonSerializerContext`; the reflection resolver is deliberately unreachable from the client.
- Config keys go in `ConfigKeys` (Shared) — no magic strings.
- Comments explain **why**, not what. The existing ones name the bug they prevent; match that.

## Deliberately removed

Do not reintroduce these without asking — each was removed for a stated reason:

- **Microsoft/Entra sign-in** — a Microsoft principal carries no GitHub credential, so it could
  sign in but not read a single repository.
- **WebGL backdrop + Web Audio feedback** (`gfx.js`, `audio.js`, their services, `SoundSettingsCard`).
- **Commit tagging** (`CommitTaggerService`, `TagsJson`) — including the badge row it fed on the
  repository detail page.
- **`POST /api/repositories`** (single add) — `/bulk` is the only write path, and it dedupes where
  the single-add path did not.
- **SmartAlerts**, **Failed Operations**, the **AI model selector** — see AGENT.MD.
