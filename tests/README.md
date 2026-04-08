# Tests

This solution keeps tests split by scope:

- `PoRepoLineTracker.UnitTests` covers application, domain, and infrastructure units without the web host.
- `PoRepoLineTracker.IntegrationTests` covers API and storage behavior through the ASP.NET host.
- `PoRepoLineTracker.E2ETests.TS` covers browser flows with Playwright.

Coverage thresholds are enforced through [coverlet.runsettings](coverlet.runsettings) for the .NET test projects.

Typical local commands:

```powershell
dotnet test tests/PoRepoLineTracker.UnitTests
docker compose up -d
dotnet test tests/PoRepoLineTracker.IntegrationTests
cd tests/PoRepoLineTracker.E2ETests.TS
npm install
npx playwright test
```