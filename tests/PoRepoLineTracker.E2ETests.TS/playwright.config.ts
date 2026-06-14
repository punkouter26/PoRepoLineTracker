import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5010',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    // Always headless to conserve MSI laptop resources (override with --headed)
    headless: true,
  },
  projects: [
    {
      name: 'edge',
      use: { ...devices['Desktop Edge'], channel: 'msedge' },
    },
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-chromium',
      use: { ...devices['Pixel 5'] },
    },
  ],
  webServer: {
    // Kill any process listening on port 5010, then start the app in the Test environment
    // (ASPNETCORE_ENVIRONMENT=Test) so E2E runs against non-AI mock data, not the live dev config.
    // NOTE: --no-launch-profile is required. Without it, `dotnet run` applies
    // launchSettings.json (Environment=Development, ports 5000/5001), which overrides the
    // ASPNETCORE_ENVIRONMENT/ASPNETCORE_URLS set here — the app would bind :5000 as Development
    // instead of :5010 as Test, and Playwright (waiting on :5010) would report "exited early".
    command: process.platform === 'win32'
      ? 'powershell -NoProfile -Command "$p = Get-NetTCPConnection -LocalPort 5010 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }; $env:ASPNETCORE_ENVIRONMENT = \'Test\'; $env:ASPNETCORE_URLS = \'http://localhost:5010\'; dotnet run --no-launch-profile --project ../../src/PoRepoLineTracker.Api"'
      : 'bash -lc "fuser -k 5010/tcp || true; export ASPNETCORE_ENVIRONMENT=Test; export ASPNETCORE_URLS=\"http://localhost:5010\"; dotnet run --no-launch-profile --project ../../src/PoRepoLineTracker.Api"',
    url: 'http://localhost:5010/health',
    reuseExistingServer: true,
    timeout: 300000,
  },
});
