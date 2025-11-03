# PoRepoLineTracker E2E Tests (TypeScript)

End-to-end tests for PoRepoLineTracker using Playwright and TypeScript.

## Prerequisites

- Node.js (v18 or higher)
- .NET 9 SDK
- GitHub Personal Access Token configured in API user secrets

## Setup

1. Install dependencies:
```bash
npm install
```

2. Install Playwright browsers:
```bash
npx playwright install chromium
```

3. Configure GitHub PAT in API user secrets:
```bash
dotnet user-secrets set "GitHub:PAT" "your-github-token" --project ../../src/PoRepoLineTracker.Api
```

## Running Tests

### Run all tests
```bash
npm test
```

### Run tests in headed mode (see browser)
```bash
npm run test:headed
```

### Run tests in debug mode
```bash
npm run test:debug
```

### Run tests with UI mode
```bash
npm run test:ui
```

### View test report
```bash
npm run test:report
```

## Test Structure

- `tests/basic-functionality.spec.ts` - Basic page navigation and health check tests
- `tests/add-repository.spec.ts` - Tests for adding repositories and viewing charts

## Configuration

Test configuration is in `playwright.config.ts`:
- **Base URL**: http://localhost:5000
- **Test Directory**: ./tests
- **Browser**: Chromium (Desktop Chrome)
- **Auto-start API**: The API server starts automatically before tests run

## Features

- ✅ Automatic API server startup
- ✅ Screenshots on failure
- ✅ Trace recording on first retry
- ✅ HTML test reports
- ✅ Full TypeScript support
- ✅ Parallel test execution

## Notes

- The API server starts automatically using the `webServer` configuration
- Tests wait for the health check endpoint before starting
- GitHub PAT is required for adding private repositories
- Tests expect PoDebateRap repository to be available in the GitHub account
