import { test, expect } from '@playwright/test';

test.describe('Basic Functionality Tests', () => {
  test('home page should load successfully', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    
    // Page should be loaded
    expect(page.url()).toBe('http://localhost:5000/');
  });

  test('should navigate to repositories page', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    
    // Navigate to repositories
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    
    expect(page.url()).toContain('/repositories');
  });

  test('should navigate to add repository page', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    
    // Navigate to add repository
    await page.goto('/add-repository');
    await page.waitForLoadState('networkidle');
    
    expect(page.url()).toContain('/add-repository');
  });

  test('health check endpoint should return healthy status', async ({ request }) => {
    const response = await request.get('/healthz');
    expect(response.ok()).toBeTruthy();
    
    const data = await response.json();
    // Status property is in the format returned by the API
    expect(data.Status || data.status).toBe('Healthy');
  });
});
