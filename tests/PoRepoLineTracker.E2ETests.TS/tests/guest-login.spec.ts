import { test, expect } from '@playwright/test';

/**
 * GUEST login golden path (Task 6 / Rule 13).
 *
 * Validates the dev/test-only GUEST bypass:
 *   1. Clicking "Login as GUEST" bypasses OAuth (never leaves for github.com).
 *   2. The server mints a username matching the GUEST + 8-random-digit schema.
 *   3. The session is persisted via LocalStorage so it survives a refresh.
 *
 * Requires the web server to run in a non-Production environment (the Playwright
 * webServer sets ASPNETCORE_ENVIRONMENT=Test), which enables EnableGuestMode.
 */

const GUEST_STORAGE_KEY = 'PoRepoLineTracker.GuestSession';
const GUEST_USERNAME = /^GUEST\d{8}$/;

test.describe('GUEST login golden path', () => {
  test('bypasses OAuth, generates GUEST+8 digits, and persists via LocalStorage', async ({ page }) => {
    await page.goto('/login');

    // The GUEST button only renders when the server-authoritative EnableGuestMode flag is on.
    const guestButton = page.getByRole('button', { name: /Login as Guest/i });
    await guestButton.waitFor({ state: 'visible', timeout: 20000 });

    await guestButton.click();

    // OAuth bypass: we return to the app shell, never redirected out to GitHub.
    await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 20000 });
    expect(page.url()).not.toContain('github.com');

    // The session reflects an authenticated GUEST with the required username schema.
    const meResponse = await page.request.get('/api/auth/me');
    expect(meResponse.ok()).toBeTruthy();
    const me = await meResponse.json();
    expect(me.isAuthenticated).toBe(true);
    expect(me.isAnon).toBe(true);
    expect(me.username).toMatch(GUEST_USERNAME);

    // LocalStorage persistence — the auth state provider writes the guest session
    // during app load; poll until it appears.
    await expect
      .poll(async () => page.evaluate((key) => localStorage.getItem(key), GUEST_STORAGE_KEY), { timeout: 10000 })
      .toBeTruthy();

    const stored = await page.evaluate((key) => localStorage.getItem(key), GUEST_STORAGE_KEY);
    expect(stored).toContain(me.username);

    // Survives a full refresh (persistence) — stays authenticated, not bounced to /login.
    await page.reload();
    await page.waitForLoadState('networkidle');
    expect(page.url()).not.toContain('/login');
  });
});
