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
    // Kill any process listening on port 5000, then start the app.
    command: process.platform === 'win32'
      ? 'powershell -NoProfile -Command "$p = Get-NetTCPConnection -LocalPort 5010 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }; $env:ASPNETCORE_URLS = \'http://localhost:5010\'; dotnet run --project ../../src/PoRepoLineTracker.Api"'
      : 'bash -lc "fuser -k 5010/tcp || true; export ASPNETCORE_URLS=\"http://localhost:5010\"; dotnet run --project ../../src/PoRepoLineTracker.Api"',
    url: 'http://localhost:5010/health',
    reuseExistingServer: true,
    timeout: 300000,
  },
});
