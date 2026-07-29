# Tests

Four tiers, matching the layout in `AGENT.MD`:

- `PoRepoLineTracker.Unit` — pure logic, no I/O.
- `PoRepoLineTracker.Integration` — API and storage behaviour through the ASP.NET host (Azurite via Testcontainers).
- `PoRepoLineTracker.E2EAPI` — API contract testing against a running host.
- `PoRepoLineTracker.E2EUI` — Playwright browser flows (mobile + desktop).

Coverage thresholds are enforced through [coverlet.runsettings](coverlet.runsettings).

Authenticating in Integration/E2E: send `X-Fake-User` (and optionally `X-Fake-Roles`) and
`FakeAuthHandler` authenticates the request. It refuses to start in Production.

Typical local commands:

```powershell
dotnet test tests/PoRepoLineTracker.Unit
docker compose up -d
dotnet test tests/PoRepoLineTracker.Integration
dotnet test tests/PoRepoLineTracker.E2EAPI
dotnet test tests/PoRepoLineTracker.E2EUI
```
