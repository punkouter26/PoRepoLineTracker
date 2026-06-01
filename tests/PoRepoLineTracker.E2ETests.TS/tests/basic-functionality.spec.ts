import { test, expect } from '@playwright/test';

test.describe('Basic Functionality Tests', () => {
  test('home page should load successfully', async ({ page }) => {
    await page.goto('/');

    const t = await page.title();
    expect(t).toContain('PoRepoLineTracker');
    expect(page.url()).toMatch(/localhost:\d+\//);
  });
});

test.describe('Page Load Quality', () => {
  test('home page has no console errors or crashes', async ({ page }) => {
    const errors: string[] = [];
    const crashes: string[] = [];
    const pageErrors: string[] = [];

    page.on('console', msg => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });

    page.on('crash', () => {
      crashes.push('Page crashed');
    });

    page.on('pageerror', (error) => {
      pageErrors.push(error.message);
    });

    await page.goto('/');
    await page.waitForTimeout(2000);

    // Report crashes
    crashes.forEach(c => console.error('CRASH:', c));
    expect(crashes).toHaveLength(0);

    // Report unhandled JS errors (Blazor WASM / JSInterop failures)
    pageErrors.forEach(e => console.error('PAGE ERROR:', e));
    expect(pageErrors).toHaveLength(0);

    // Filter out known Blazor WASM loading noise and expected auth/CORS redirects
    const realErrors = errors.filter(e =>
      !e.includes('Failed to load resource') &&
      !e.includes('net::ERR_') &&
      !e.includes('blazor.web.js') &&
      !e.includes('CORS policy') &&
      !e.includes('Access-Control-Allow-Origin')
    );

    realErrors.forEach(e => console.error('CONSOLE ERROR:', e));
    expect(realErrors).toHaveLength(0);
  });
});
