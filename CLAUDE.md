# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This file is what you need to not waste a cycle. There is no separate architecture document:
`AGENT.MD` was deleted in 462ee8f, and the rationale that survived it lives here and in the
comments on the types themselves — those comments name the bug they prevent, and are authoritative.

`NET_RULES.md` holds the current numbered house rules (1.1–3.4). In-code comments used to cite
an older, incompatible numbering ("Rule 4.2", "Rule 13", …); those dangling citations have been
removed — comments now carry their rationale inline, and that inline rationale is authoritative.

## What this is

Blazor WASM + ASP.NET Core (net10.0) that tracks lines of code across a user's GitHub
repositories over time: line-count history, per-extension composition, and contributor stats.
Sign-in is **GitHub OAuth only**.

**There is no AI in this app.** No LLM call, no local model runtime, no AI/ML package, no
inference endpoint, no API key for one. The AI-authorship score this file used to describe was a
local heuristic, and it has been deleted along with the rest of the `AiDetection` slice. If a
request arrives about "our AI costs" or "inference latency", the answer is that there is nothing
to optimise — check before taking the premise.

## Projects

```
PoRepoLineTracker.Shared   DTOs, domain models, strongly-typed IDs, validation. LEAF — no project refs.
PoRepoLineTracker.API      Minimal API + storage + feature slices. Also serves the WASM client.
PoRepoLineTracker.Client   Blazor WASM. Depends on Shared only; talks to the API over HTTP.
```

Everything in Shared is under `PoRepoLineTracker.Shared.*` — one prefix, no exceptions.
`Shared/Domain/` (and the strongly-typed IDs, which live there too) used to declare
`PoRepoLineTracker.Domain.Models`, so most consuming files carried two `using` lines where one
would do and the folder name did not predict the namespace.

Vertical slices under `API/Features/{Name}/` own their endpoints, commands/queries and handlers
together. **Slices must not reference each other** — `GlobalUsings.cs` deliberately omits
`Features.*` so cross-slice coupling needs an explicit `using`, and only `Extensions/` (the
composition root) has one.

Outside the slices, the API splits by what the code does rather than by layer:

```
API/Analysis/    Walks a repository and counts it — GitHubService, GitClient, FileIgnoreFilter.
API/Storage/     Azure Table Storage entities and the services that read/write them. Nothing else.
API/Services/    Measuring and scoring — the metrics analysers, the line counter, progress.
```

`Analysis/` was carved out of `Storage/`: the three files in it are the largest in the API and none
of them touches a table, so the folder name was actively misleading about the biggest thing under
it. Both are in `GlobalUsings.cs`, so moving a type between them does not ripple.

## Build, run, test

```bash
docker compose up -d                                   # Azurite (Table Storage) + Jaeger (traces)
dotnet run --project src/PoRepoLineTracker.API --launch-profile https   # https://localhost:5003

dotnet build
dotnet test tests/PoRepoLineTracker.Unit          # 89 — no external deps
dotnet test tests/PoRepoLineTracker.Integration   # 46 — WebApplicationFactory + Testcontainers Azurite
dotnet test tests/PoRepoLineTracker.E2EAPI        # 25 — needs the app running
dotnet test tests/PoRepoLineTracker.E2EUI         # 22 — needs the app running + Playwright (~3m30s)
```

Those four counts are capped at 100 / 50 / 25 / 25 respectively. The cap is the point: a suite that
grows without bound stops being run before a commit, and an unrun test is worse than an absent one
because it still reads as coverage. Adding a test past a cap means deleting a weaker one in the
same tier, not raising the number here.

**Traces**: `docker compose` also runs Jaeger. `OpenTelemetry:OtlpEndpoint` in
`appsettings.Development.json` points at it, so every request, outbound call and analysis step
shows up as a waterfall at <http://localhost:16686>. The exporter is registered only when that key
is non-blank — leave it blank and telemetry is built and dropped, which is what "OTLP Exporter —
Not configured" on `/diag` means.

**Application Insights needs a local secret.** A connection string must never be committed, so
`appsettings.Development.json` carries no `ApplicationInsights` section at all — the value lives in
user-secrets. On a fresh clone:

```bash
CS=$(az monitor app-insights component show \
       --app poappideinsights8f9c9a4e -g PoShared --query connectionString -o tsv)
dotnet user-secrets set "ApplicationInsights:ConnectionString" "$CS" \
       --project src/PoRepoLineTracker.API
```

Without it the app runs fine and `/diag` reports App Insights as "Not configured" — accurate, not a
bug. `TelemetrySettings` is the single resolver both `AddTelemetry` and `/diag` use, so the page
cannot disagree with what is actually registered; it previously checked only the
`APPLICATIONINSIGHTS_CONNECTION_STRING` env form and so reported "Not configured" for a
perfectly-configured app.

All four tiers are **green with zero skips**. Keep it that way — a skip in E2EUI now means the app
isn't up, not that a fixture is missing.

**E2EUI is timing-sensitive under machine load.** A clean run is ~2 minutes. When the box is busy
(other containers, a build running alongside), the run stretches past 3 minutes and one or two
tests fail on a wait that expired — `Chart_ResponsiveStepAndHeight` measuring a chart box as 0, or
the service-worker wait in `InstallUiTests` timing out. Both pass in isolation and the failure
moves between runs. Before treating an E2EUI failure as a regression, re-run the one test alone:
if it passes, the suite was starved, not broken.

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

**The service worker must never answer a server route from the cached shell.** The stock Blazor
template serves `index.html` for every navigation. Here that breaks sign-in outright: the GitHub
OAuth round-trip is a sequence of top-level navigations, and returning the Blazor shell in place of
one strands the user on a page that thinks it is logged out. `service-worker.published.js` keeps a
`serverRoutePrefixes` list (`/api/`, `/auth/`, `/hubs/`, `/health`, `/signin-`, `/signout-`) that
goes straight to the network. `service-worker.js` — the development one — is a deliberate no-op,
because a worker serving yesterday's `_framework/*.wasm` is indistinguishable from a build that
silently did not take. The swap happens via the `<ServiceWorker>` item in the client csproj, and
`InstallUiTests` asserts the dev worker holds no cache, which is what proves the swap is
real rather than the published file having been served all along.

**The install button is driven by an event, not a query.** There is no "is this installable" API;
the only signal is `beforeinstallprompt`, which fires once, does not replay, and can fire before
the WASM runtime has started. It is therefore captured in the boot script in `index.html`, not in a
module the app imports later, and `InstallAppButton` subscribes through
`registerInstallListener` — which returns the *current* availability as well as subscribing, so a
component mounting after the event still gets the right answer. `worker-src` and `manifest-src` are
named explicitly in the CSP rather than left to fall back through `script-src`, so tightening
`script-src` later cannot silently take the worker with it.

**Line counting is memoised on git object ids — never hand a caller the memo's own dictionary.**
`GitHubService` caches per-tree and per-blob counts for the lifetime of its (scoped) instance,
which is one analysis run. Git trees are content-addressed, so an unchanged directory between two
commits is one dictionary lookup rather than a recursive walk — replaying history used to
decompress every blob of every commit, making analysis scale with commits × repository size. The
tree memo is keyed on object id **and path** (the ignore filter's answers depend on where a
directory sits), it is dropped when the counted-extension set changes, and
`CountLinesInCommitAsync` returns a **copy** — a caller mutating a memoised instance would poison
every later commit sharing that tree. `GitHubServiceCountingTests` drives a real on-disk
repository and pins all of it, including that an unchanged file is read exactly once across a
multi-commit replay.

**Never add a per-item storage lookup that a loop will call.** `CommitExistsAsync` was removed for
this: it was a point read per SHA, called once per commit, so a 4,000-commit repository paid 4,000
round-trips (and 8,000 Information log lines) to answer what one
`GetCommitLineCountsByRepositoryIdAsync` already returns for the whole repository. The analysis
handler pre-loads that set unconditionally and looks SHAs up in memory.

**Every page owns an `<h1>`, a `<PageTitle>`, and lives inside the layout's `<main>`.** All three
were missing on every route at once: the header's brand wordmark was an `<h5>`, so document
outlines ran H5 → H3 → H6 with the brand outranking the page, and every route shared the title
"PoRepoLineTracker". The page title is a real `<h1 class="page-hero__title">` written in the page's
own markup — `PageHero` cannot change the tag of a fragment handed to it, which is why that class
is styled globally in `app.css` rather than in `PageHero.razor.css`. `AccessibilityUiTests` asserts
one h1, one main landmark, and a distinct title per route.

**The skip link moves focus in code, not by fragment navigation.** Blazor's router intercepts
anchor clicks, so `href="#main-content"` scrolled the page and left focus in the nav — a skip link
that looks like it works and does nothing for the keyboard user it exists for. `MainLayout` holds
an `ElementReference` to `<main>` (focusable only because of its `tabindex="-1"`) and calls
`FocusAsync` on click with `preventDefault`.

**Production behaves differently from every tier that tests it.** `ProductionAuthEnforcementMiddleware`
is a no-op outside Production, and Integration runs as `"Test"` while both E2E tiers run as
Development — so *nothing that drives a real host executes its Production branch*. That is how it
came to challenge the OAuth provider directly: locally `/` returned 200, the deployed site returned
a 302 to github.com, and no test could see the difference. It now redirects to the app's own
`/login` (same origin, so the installed PWA's `start_url: "/"` stays in scope), and it is
**unit-tested against a substituted `IWebHostEnvironment`** — the only place that branch runs.
Anything else added to that middleware needs the same treatment.

**Never add a catch-all endpoint under `/api`.** `ApiNotFoundMiddleware` answers 404 for an
unmatched `/api` GET, which previously fell through to the SPA fallback and returned 200 carrying
an HTML document to a JSON caller. It is *middleware, not a route*, because the obvious
`MapFallback("/api/{**rest}", …)` was tried and reverted: a catch-all becomes a routing candidate
beside the real routes and won for some of them, so `POST /api/repositories/bulk` resolved to the
catch-all, passed authorization (a catch-all must be anonymous) and came back 400 from the
antiforgery gate instead of 401. Route precedence is not worth betting an authorization outcome on.
The middleware covers GET only — see the known limit documented on it.

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

**Vendored third-party code is excluded, and one rule does it by CONTENT not name.**
`FileIgnoreFilter` prunes a directory when its immediate children include two or more
repository-root markers (`LICENSE*`, `.github`, `.gitmodules`, `CODEOWNERS`, `CODE_OF_CONDUCT.md`,
`.pre-commit-config.yaml`, …) — that is a whole other project copied in, whatever folder name the
author gave it. Name-based rules cannot catch this: the case it was written for was Unity's
ml-agents vendored under `Training/`, where the old reverse-domain rule caught only the
`com.unity.ml-agents/` subfolder and left ~78% of the repository's counted lines as third-party.
Requires the entry-aware `ShouldIgnoreDirectory(path, entryNames)` overload — the path-only one
cannot see children. **Filter changes only affect new analysis; stored counts need a re-analyze.**

**Comment syntax is configured per language family, and CSS is not C.** `SourceLineCounter.DefaultSet()`
groups extensions by the syntax they actually use. Plain `.css` has **no** line-comment form — only
`/* */` — and was registered with `//`, which truncated every line from its first `//` onward, so
`url(https://…)` lost its tail and a line holding nothing else counted as blank. `.scss`/`.less` do
have `//`, which is exactly why they cannot share an entry with `.css`; `.razor`/`.cshtml` use
`@* *@` and `<!-- -->`, never `//`.

**Code health has two analysers, split by language — never run both over one file.** `.cs` is
PARSED by `RoslynMetricsAnalyzer` and produces Visual Studio's real Code Metrics; every other
language is ESTIMATED by `CodeMetricsAnalyzer` on six line-oriented factors (branch density,
nesting, file size, comment ratio, debt markers, line length). Sending C# to both would put two
maintainability numbers on one repository, and a figure that reads differently in two places is
indistinguishable from a data bug. `GetCodeHealthQuery` does the routing; `GetCodeHealthQueryHandlerTests`
pins which analyser sees what. For the estimated half the ranking is the reliable part and the
absolute number is a guide — `CodeHealthCard` says so on the card, not in a tooltip.

**Maintainability Index does NOT need a build — this file used to claim it did.** The old reasoning
here was that VS's index "needs Halstead volume and a control-flow graph, which means a parser per
language and a resolved compilation". Halstead volume is purely **lexical** — operator and operand
counts off the token stream — and cyclomatic complexity comes straight off the syntax tree, so both
are computed for C# with no SDK, no MSBuild and no restore. That is what lets this run on the F1
App Service, which is a runtime-only container that cannot build anything.

**The two metrics that genuinely need a compilation are not reported.** Depth of Inheritance and
Class Coupling need resolved type references, because a base or referenced type can live in another
assembly. Reaching them means `MSBuildWorkspace`, which needs the SDK present and a restore of the
repository being measured — and building an arbitrary repository EXECUTES its code through MSBuild
targets and NuGet scripts, which a shared web host must not do on a stranger's behalf. It also drags
in `Microsoft.Build` packages currently carrying high-severity advisories, and this solution builds
with `TreatWarningsAsErrors`. If those two are ever wanted, the honest route is an opt-in local
agent, not a web request.

**The Maintainability Index is per MEMBER, then averaged — not computed over repository totals.**
The published formula was calibrated on one unit of code. Fed whole-repository figures, the
`0.23 * complexity` term alone reaches several hundred and every repository of any size clamps to
0 — a unit error that looks exactly like a verdict. This was written wrong first and caught by the
spike; `MaintainabilityIndex_IsAveragedPerMember_NotComputedOverWholeFileTotals` is the regression
test. Measured correctly, this repository scores 87.

Three things about it that are load-bearing:

- **Measuring and judging are separate types.** `CodeMetricsAnalyzer` counts; `CodeHealthScoring`
  judges. Re-weighting the report must not risk changing what was counted.
- **Nesting is the 90th-percentile line depth, not the maximum.** One wrapped argument list is
  indented far past the structure around it, and using the max let a 40-line middleware holding a
  wrapped CSS string measure 14 levels deep and outrank genuinely tangled files.
- **Markup is judged on its own nesting band.** Nested components are not a smell, and scoring
  `.razor` against the code band gave this repository's own markup a 48 against 94 for its C# —
  a fact about the file format, not the code.

**Anything derived from a commit is memoised per SHA, never recomputed.** A commit is immutable, so
every figure computed from its tree — maintainability, complexity, line counts, the combined score —
is a constant. `CodeHealthSnapshotEntity` stores them keyed on **(repositoryId, commitSha)**, with
the whole report serialized alongside the scalars, so a repeat view is answered from storage without
opening the repository at all. Three consequences worth knowing: the same row serves the current
report AND every point on the monthly trend, so nothing is measured twice under two names; it works
on a host whose clone has been recycled away, which the on-demand path cannot; and `ScoringVersion`
exists so a formula change invalidates old rows rather than letting one chart mix two arithmetics.
**Load the memo in ONE partition query before the loop** — a point read per SHA is the mistake that
got `CommitExistsAsync` deleted.

**The health trend measures the last commit on or before each 1st.** Not the nearest commit either
side: the point is "what did the code look like that morning". A month with no commit before it is
absent, never zero — zero on a 0-100 health axis reads as "terrible" rather than "not yet". Quiet
months resolve to the same commit as the previous one, which is correct and shows as a flat run.
A repository whose whole history sits inside one month has commits but crosses no boundary, and that
returns an **empty series, not a 404** — reporting "no analysed commits yet" for a repository being
actively committed to is simply false.

**Comment syntax lives in one table.** `CommentSyntax` maps extension → line/block markers, and both
`SourceLineCounter` (which discards comments) and `CodeMetricsAnalyzer` (which counts them, and must
strip them before looking for branch keywords) read it. Two copies would drift, and both sides would
go on producing plausible numbers. Registrations are grouped by family because listing them one at a
time is how `.css` and `.razor` ended up with C-style `//` line comments they do not have.

**Repository ownership is checked by `Auth/RepositoryOwnership`, not by a slice.** Any route taking
a repository id from the URL needs it, repository ids appear in more than one slice, and slices may
not reference each other — so leaving it as a private helper meant the second slice to need it would
copy it. A copied authorization check fails quietly: the copy stops matching and the symptom is a
route serving another user's data, not a broken build.

**Contributor share is weighted by lines added**, not averaged per commit — a one-line commit and
a 2,000-line refactor are not the same event. (This rule used to describe an "AI share"; the
weighting survived the removal of AI detection because `GetContributorStatsQuery` still applies
it.)

**Analysis progress has one path.** The SignalR hub (`/hubs/analysis`) pushes frames; the fallback
poll in `Repositories.razor` exists only for when the hub is unreachable, and it *synthesises the
same frames* rather than handling completion itself. Do not add a second completion path — there
used to be three, and they had drifted.

**Progress frames carry live tallies, and the collection on them is replaced, never mutated.**
`AnalysisProgressDto` now also carries `StartedUtc`, `LinesCounted` and `Extensions`, so
`AnalysisActivityFeed` can show a stage rail, a running line count, throughput and an ETA instead
of a percentage that creeps a point every few seconds. `Publish` serialises that object on a
fire-and-forget task while the loop keeps reporting, so appending to `Extensions` in place would
throw mid-serialization — the analysis loop hands over a fresh list each time. `LinesCounted` is
cumulative churn across replayed commits, not a repository size, and the UI labels it that way.

**Saved preferences live in `UserPreferencesClient`**, which raises `Changed` after a write. Pages
read from it and subscribe; they do not render their own copy of a settings control.

**Streaks and per-extension snapshots have one definition each, in Shared.** `CommitStreaks`
(current/longest run of active days) and `RepositoryTotals.LinesByFileTypeAsOf` sit next to
`RepositoryTotals` for the reason stated there: more than one surface prints a streak — Insights
and the digest banner today — and slices may not reference each other, so the next one to want it
would have no shared home to reach for. Without one each would grow its own copy, and a streak
reading 12 on one page and 11 on another is indistinguishable from a data bug.

**Language drift is measured in share, not lines.** A file type's share of the portfolio can grow
while the type itself shrinks — everything else shrank faster. It is the one figure on Insights
that the language-mix card beside it cannot show, and a line-count delta reads it as a loss. Its
window is **trailing 365 days**, like every other window on that page; `GetPortfolioInsightsQuery`
computes it in the loop it already runs, off `RepositoryTotals.LinesByFileTypeAsOf`. Rows that did
not move are dropped rather than ranked last — a card whose every row reads "70% → 70%, +0.0 pts"
under a sentence with nothing to name is worse than no card.

**The digest's read and its "mark seen" write are separate calls, deliberately.**
`GET /api/insights/digest` never records the visit; `POST /api/insights/digest/seen` does, and the
banner only calls it once it has actually rendered. Merge them and a page opened and closed without
the user looking consumes the window, leaving the next real visit with nothing to report. The write
is read-modify-write because `SavePreferencesAsync` upserts with `TableUpdateMode.Replace` — build
the preferences object from anything less than the stored row and recording a visit silently blanks
the user's counted-extensions list. `DigestTests` pins exactly that.

**A last visit is only used when it is between 12 hours and 90 days old.** Anything more recent
falls back to a trailing 7 days: someone who reloaded twenty minutes ago has no news, and a banner
announcing "0 commits since you were last here" looks broken on the one path — an engaged user —
where it most needs not to. Anything older falls back too: a banner is the wrong surface for a
year's worth of news, and the dashboard behind it already reports the year.

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
- **SmartAlerts**, **Failed Operations**, the **AI model selector**. (The reasoning lived in
  AGENT.MD, which no longer exists — treat the removal itself as the decision, and ask before
  reviving any of the three.)
- **The Year in Code recap** (`/recap`, `Features/Recap/`, `YearInCodeDto`, `Recap.razor` and its
  436-line stylesheet) — ~1,400 lines for a page built around a once-a-year novelty, whose figures
  were a dozen hand-rolled bar charts in bespoke CSS that nothing else reused. Its one genuinely
  non-obvious figure, language drift, moved to Insights as a card on a trailing window; the rest
  (peak hour, weekday rhythm, biggest single commit) went with the page.
- **The mobile bottom-nav bar and the breadcrumb row** (`MainLayout`) — a second and third copy of
  navigation the drawer already provides. The app is two levels deep at its worst, so on every
  route but the detail page the breadcrumb was a single non-link naming the `<h1>` directly beneath
  it, and the bar permanently consumed 56px of every phone screen. `NavMenu.razor` is now the only
  list of destinations, which is the point: a second copy is a second thing to keep in sync.
- **`/settings/extensions-counted`** — a second `@page` on the Settings screen that nothing linked
  to. One screen under two names is a routing table that has to be read to be trusted.
