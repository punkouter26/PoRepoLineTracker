import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    headless: true,
  },
  projects: [
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
      ? 'powershell -NoProfile -Command "$p = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }; $env:ASPNETCORE_URLS = \'http://localhost:5000\'; dotnet run --project ../../src/PoRepoLineTracker.Api"'
      : 'bash -lc "fuser -k 5000/tcp || true; export ASPNETCORE_URLS=\"http://localhost:5000\"; dotnet run --project ../../src/PoRepoLineTracker.Api"',
    url: 'http://localhost:5000/health',
    reuseExistingServer: true,
    timeout: 300000,
  },
});
