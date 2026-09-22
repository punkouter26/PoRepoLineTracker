# CI gates — `apps/GitHub Actions/deploy.yml`

## Test tier choice (Unit-only)

The `build` job runs **`tests/PoRepoLineTracker.Unit` only**. The other tiers
each have a hard CI-blocking dependency the hosted runner cannot satisfy, and
adding them was tried before:

- **Integration** (`tests/PoRepoLineTracker.Integration`) — needs Testcontainers
  spinning up Azurite per test class. On a hosted Linux runner the
  Docker-in-Docker path is flaky enough to fail most PRs for reasons unrelated
  to the change. 50 tests, ~7s locally with Azurite already up; on a cold
  runner with image pulls and Testcontainers init it is 2-3 minutes and
  intermittent.

- **E2EAPI / E2EUI** — both need a running app (`dotnet run` from the same job)
  plus, for E2EUI, Playwright browsers installed via the bootstrapping script.
  Combined wall-clock ~6 minutes, and the failure modes are usually "the
  runner could not bind to a port" or "browser install timed out", not real bugs.

All three tiers run green from a developer machine with `docker compose up -d`
and the app already built; the unit suite is the gate this pipeline CAN
honestly keep honest, so it is the gate it keeps. If you ever wire up a
self-hosted runner with persistent Azurite + Playwright, add
`if: runner.dedicated == true` here rather than removing the comment — the WHY
is the part that needs to survive.

## Test-suite size caps

The CI does not enforce a hard cap on test-suite sizes — see the history
of `Enforce test-suite caps` in `.github/workflows/deploy.yml` for the
removed step. Tier sizes are still expected to stay small (Unit ~100,
Integration ~50, E2E tiers ~25) so the unit test stays fast enough to
gate every commit. A tier creeping toward its informal limit should
trigger a "merge or prune before adding" pass, not a cap raise.

`[Fact]` and `[Theory]` rows are counted at the source level (a Theory
row counts as one). The counter was a grep, not a test — it did not live
inside the thing it counted.

The reason the step was removed rather than fixed: the underlying bash
(`grep -c ... | awk ...` under `set -euo pipefail`) exits 1 when a
project has zero matches for the pattern, which the E2E projects do —
they use `[SkippableFact]` instead of `[Fact]`. The pipeline was
failing on the cap step on every run, not on real cap overruns. With the
step gone the underlying assumption ("suite sizes stay small") is
preserved as convention rather than enforced as a gate.

## Why unit tests pass before commit is not enough

`tests/PoRepoLineTracker.Unit` is pure logic. Composition-root changes
(`Program.cs`, `AddInfrastructure`, `AddAuth`, middleware order) only show up at
`Integration` tier, where the host is actually wired. Run Unit + Integration
locally before pushing — the unit-only gate exists for CI determinism, not
because Unit is the only tier that matters.

## Bicep compile gate

The `lint-infra` job compiles every `infra/*.bicep` to ARM on every run —
fast, no Azure login, and it catches template/type errors (and the kind of
drift that broke prod) before any merge. It runs in parallel with `build` so it
does not add wall-clock time.
