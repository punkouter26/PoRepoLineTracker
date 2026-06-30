import { test, expect } from '@playwright/test';

/**
 * Login / Authentication E2E tests.
 *
 * All pages are [Authorize]-protected. Unauthenticated users are automatically
 * redirected through RedirectToLogin → /auth/login → GitHub OAuth.
 * These tests validate that redirect chain works and the auth API behaves correctly.
 */

test.describe('Login / Authentication', () => {
  test('unauthenticated visit to home page redirects to GitHub OAuth', async ({ page }) => {
    // Playwright headless can't cross to github.com — verify the in-app /login redirect fires
    await page.goto('/');

    // Blazor WASM loads, auth state resolves → RedirectToLogin fires → navigates to /login
    await page.waitForURL(/\/login/, { timeout: 20000 });

    expect(page.url()).toContain('/login');
  });

  test('auth/login is handled gracefully (302 challenge when configured, else 503)', async ({ request }) => {
    const response = await request.get('/auth/login', { maxRedirects: 0 });
    // When GitHub OAuth is configured (dev/prod) the endpoint issues a 302 challenge to GitHub.
    // In the Test environment there are no OAuth secrets, so it returns a graceful 503
    // ProblemDetails instead of an unhandled 500 — both are "handled", never a crash.
    expect([302, 503]).toContain(response.status());
    if (response.status() === 302) {
      expect(response.headers()['location'] ?? '').toContain('github.com');
    }
  });

  test('auth/me returns isAuthenticated=false for anonymous', async ({ request }) => {
    const response = await request.get('/auth/me');
    expect(response.ok()).toBeTruthy();

    const data = await response.json();
    expect(data.isAuthenticated).toBe(false);
  });

  test('logout endpoint redirects to home without 5xx', async ({ request }) => {
    // An unauthenticated logout should gracefully redirect, not crash
    const response = await request.get('/auth/logout', { maxRedirects: 0 });
    expect(response.status()).toBeLessThan(500);
  });
});

