import { test, expect } from '@playwright/test';

test.describe('Basic Functionality Tests', () => {
  test('home page should load successfully', async ({ page }) => {
    await page.goto('/');

    const t = await page.title();
    expect(t).toContain('PoRepoLineTracker');
    expect(page.url()).toMatch(/localhost:\d+\//);
  });

  test('health check endpoint should return healthy status', async ({ request }) => {
    const response = await request.get('/health');
    expect(response.ok()).toBeTruthy();
    // /health returns plain text "Healthy"
    const text = await response.text();
    expect(text).toBe('Healthy');
  });
});

test.describe('Public API Endpoints', () => {
  test('file-extensions endpoint returns array', async ({ request }) => {
    const response = await request.get('/api/settings/file-extensions');
    expect(response.ok()).toBeTruthy();
    const data = await response.json();
    expect(Array.isArray(data)).toBeTruthy();
    expect(data.length).toBeGreaterThan(0);
  });

  test('chart max-lines endpoint returns number', async ({ request }) => {
    const response = await request.get('/api/settings/chart/max-lines');
    expect(response.ok()).toBeTruthy();
    const data = await response.json();
    expect(typeof data).toBe('number');
    expect(data).toBeGreaterThan(0);
  });

  test('diagnostics endpoint returns environment info', async ({ request }) => {
    const response = await request.get('/diag');
    expect(response.ok()).toBeTruthy();
    const text = await response.text();
    expect(text).toContain('environment');
  });

  test('unknown API route does not return 500', async ({ request }) => {
    const response = await request.get('/api/nonexistent-route-xyz');
    expect(response.status()).toBeLessThan(500);
  });

  test('auth/me endpoint returns response', async ({ request }) => {
    const response = await request.get('/api/auth/me');
    expect(response.status()).toBeLessThan(500);
  });
});

test.describe('Page Load Quality', () => {
  test('home page has no console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });

    await page.goto('/');
    await page.waitForTimeout(2000);

    // Filter out known Blazor WASM loading noise and expected auth/CORS redirects
    const realErrors = errors.filter(e =>
      !e.includes('Failed to load resource') &&
      !e.includes('net::ERR_') &&
      !e.includes('blazor.web.js') &&
      !e.includes('CORS policy') &&
      !e.includes('Access-Control-Allow-Origin')
    );

    expect(realErrors).toHaveLength(0);
  });
});
