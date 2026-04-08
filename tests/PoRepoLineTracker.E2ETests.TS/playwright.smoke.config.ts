import { defineConfig, devices } from '@playwright/test';

// Minimal config for temp smoke tests against existing running server
export default defineConfig({
  testDir: './',
  testMatch: 'temp-smoke-run-*.spec.ts',
  fullyParallel: false,
  retries: 0,
  reporter: 'line',
  use: {
    baseURL: 'http://localhost:5010',
    headless: true,
    trace: 'off',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  // No webServer: we rely on the already-running dev server on :5010
});
