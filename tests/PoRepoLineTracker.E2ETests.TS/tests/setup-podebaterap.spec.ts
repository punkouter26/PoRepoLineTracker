import { test, expect } from '@playwright/test';

/**
 * Setup test - Adds PoDebateRap repository to the database
 * This test should be run first to set up data for other tests
 */
test.describe('Setup - Add PoDebateRap Repository', () => {
  
  test('should add PoDebateRap repository from GitHub', async ({ page }) => {
    // Navigate to add repository page
    await page.goto('/add-repository');
    await page.waitForLoadState('networkidle');

    // Wait for GitHub repositories to load
    // Look for the loading spinner to appear and disappear
    const loadingSpinner = page.locator('.loading-spinner, [class*="loading"]').first();
    
    // Wait for loading to start (if it hasn't already)
    await page.waitForTimeout(1000);
    
    // Wait for loading to complete - either spinner disappears or repositories appear
    await Promise.race([
      loadingSpinner.waitFor({ state: 'hidden', timeout: 30000 }).catch(() => {}),
      page.locator('label.repository-label').first().waitFor({ state: 'visible', timeout: 30000 })
    ]);

    // Additional wait to ensure all repositories are loaded
    await page.waitForTimeout(2000);

    // Find and click the PoDebateRap repository checkbox
    // Note: There's a bug where all checkboxes have id="repo-0" because repo.Id is not set
    // We need to use a more specific selector to find the correct checkbox
    
    // Locate the PoDebateRap repository item by finding the container that has its label
    const repoContainer = page.locator('.repository-item:has(label:has-text("PoDebateRap"))');
    await expect(repoContainer).toBeVisible({ timeout: 10000 });
    
    // Click the checkbox within that specific repository container
    const checkbox = repoContainer.locator('input[type="checkbox"]');
    await checkbox.click();
    
    // Wait a moment for the Blazor state to update
    await page.waitForTimeout(1000);
    
    // Verify checkbox is now checked
    await expect(checkbox).toBeChecked();
    
    console.log('PoDebateRap checkbox clicked and verified as checked');

    // Click the "Add Selected" button
    const addButton = page.locator('button:has-text("Add Selected"), button:has-text("Add Repository")');
    await expect(addButton).toBeVisible();
    await addButton.click();

    // Wait for the repository to be added
    // This could take a while as it clones and analyzes the repository
    console.log('Waiting for repository to be added and analyzed...');
    
    // Look for the success message that appears after adding
    // The message should contain text like "repositories added successfully"
    await page.waitForSelector('text=/added successfully/i', { timeout: 120000 }); // 2 minutes for clone and analysis
    
    console.log('Success message appeared, waiting a moment for completion...');
    await page.waitForTimeout(5000); // Wait longer for database operations

    // Navigate to repositories page to verify
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Debug: Take screenshot and log page content
    await page.screenshot({ path: 'test-results/repositories-page-after-add.png', fullPage: true });
    
    const pageContent = await page.content();
    console.log('Page title:', await page.title());
    console.log('Looking for PoDebateRap in page content...');
    console.log('Page contains "PoDebateRap":', pageContent.includes('PoDebateRap'));
    console.log('Page contains "No repositories":', pageContent.includes('No repositories'));
    
    // Check if there are any table rows
    const tableRows = await page.locator('tbody tr').count();
    console.log(`Number of table rows: ${tableRows}`);
    
    // Log the content of each table row
    for (let i = 0; i < tableRows; i++) {
      const rowText = await page.locator('tbody tr').nth(i).textContent();
      console.log(`Row ${i}: ${rowText}`);
    }
    
    // Check for error messages
    const errorMessage = page.locator('.alert-danger');
    if (await errorMessage.isVisible()) {
      console.log('Error message found:', await errorMessage.textContent());
    }

    // Verify PoDebateRap appears in the repositories list
    // The repository name appears in a table cell, not a link
    const repoCell = page.locator('td:has-text("PoDebateRap")');
    await expect(repoCell).toBeVisible({ timeout: 10000 });

    console.log('✅ PoDebateRap repository successfully added!');
  });

  test('should verify PoDebateRap repository exists and has data', async ({ page }) => {
    // Navigate to repositories page
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');

    // Check if PoDebateRap exists
    const repoCell = page.locator('td:has-text("PoDebateRap")');
    
    const isVisible = await repoCell.isVisible();
    if (!isVisible) {
      test.skip(true, 'PoDebateRap not found - run the add repository test first');
      return;
    }

    // Click the "Show Chart" button for PoDebateRap
    const showChartButton = page.locator('button:has-text("Show Chart")').first();
    await showChartButton.click();
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Verify chart elements exist
    const svgChart = page.locator('svg').first();
    await expect(svgChart).toBeVisible({ timeout: 10000 });

    // Verify chart has data elements
    const chartElements = page.locator('svg path, svg line, svg circle, svg rect');
    const elementCount = await chartElements.count();
    expect(elementCount).toBeGreaterThan(0);

    console.log(`✅ PoDebateRap repository has ${elementCount} chart elements`);
  });

  test('should verify health check after adding repository', async ({ page }) => {
    const response = await page.request.get('/healthz');
    expect(response.ok()).toBeTruthy();
    
    const data = await response.json();
    expect(data.Status || data.status).toBe('Healthy');
  });
});
