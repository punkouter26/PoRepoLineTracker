import { test, expect } from '@playwright/test';

/**
 * Login / Authentication E2E tests.
 *
 * All pages are [Authorize]-protected. Unauthenticated users are automatically
 * redirected through RedirectToLogin → /api/auth/login → GitHub OAuth.
 * These tests validate that redirect chain works and the auth API behaves correctly.
 */

test.describe('Login / Authentication', () => {
  test('unauthenticated visit to home page redirects to GitHub OAuth', async ({ page }) => {
    // Playwright headless can't cross to github.com — verify the in-app /login redirect fires
    await page.goto('/');

    // Blazor WASM loads, auth state resolves → RedirectToLogin fires → navigates to /login
    await page.waitForURL(/\/login/, { timeout: 15000 });

    expect(page.url()).toContain('/login');
  });

  test('api/auth/login endpoint redirects (not a server error)', async ({ request }) => {
    // The endpoint should issue a 302 challenge redirect toward GitHub, never a 5xx
    const response = await request.get('/api/auth/login', { maxRedirects: 0 });
    // 302 = OAuth challenge redirect is expected
    expect(response.status()).toBe(302);
    const location = response.headers()['location'] ?? '';
    expect(location).toContain('github.com');
  });

  test('api/auth/me returns isAuthenticated=false for anonymous', async ({ request }) => {
    const response = await request.get('/api/auth/me');
    expect(response.ok()).toBeTruthy();

    const data = await response.json();
    expect(data.isAuthenticated).toBe(false);
  });

  test('logout endpoint redirects to home without 5xx', async ({ request }) => {
    // An unauthenticated logout should gracefully redirect, not crash
    const response = await request.get('/api/auth/logout', { maxRedirects: 0 });
    expect(response.status()).toBeLessThan(500);
  });
});

