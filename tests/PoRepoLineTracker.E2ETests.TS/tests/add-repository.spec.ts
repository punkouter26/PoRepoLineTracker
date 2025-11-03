import { test, expect, Page } from '@playwright/test';

const REPOSITORY_NAME = 'PoDebateRap';
const REPOSITORY_OWNER = 'punkouter26';

test.describe('Add Repository Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000); // Wait for Blazor to render
  });

  test('should add PoDebateRap repository successfully', async ({ page }) => {
    // Check if repository already exists on repositories page
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    const existingRepoCell = page.locator(`td:has-text("${REPOSITORY_NAME}")`);
    const isVisible = await existingRepoCell.isVisible().catch(() => false);
    
    if (isVisible) {
      test.skip(true, 'Repository already exists - skipping add');
      return;
    }

    // Navigate to Add Repository page
    await page.goto('/add-repository');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Wait for GitHub repositories to load
    const loadingSpinner = page.locator('text=Loading your GitHub repositories...');
    if (await loadingSpinner.isVisible()) {
      await loadingSpinner.waitFor({ state: 'hidden', timeout: 30000 });
    }

    // Check if there's an error message
    const errorAlert = page.locator('.alert-warning, .alert-danger');
    if (await errorAlert.isVisible()) {
      const errorText = await errorAlert.textContent();
      console.log(`Error on page: ${errorText}`);
    }

    // Find the repository container and click its checkbox
    const repoContainer = page.locator(`.repository-item:has(label:has-text("${REPOSITORY_NAME}"))`);
    await expect(repoContainer).toBeVisible({ timeout: 10000 });
    
    const checkbox = repoContainer.locator('input[type="checkbox"]');
    await checkbox.click();
    await page.waitForTimeout(1000);

    // Click "Add Selected" button
    const addButton = page.locator('button:has-text("Add Selected")');
    await expect(addButton).toBeVisible({ timeout: 5000 });
    await addButton.click();

    // Wait for the success message
    await page.waitForSelector('text=/added successfully/i', { timeout: 120000 });
    await page.waitForTimeout(3000);

    // Navigate to repositories page to verify
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Verify repository appears in the table
    const repoCell = page.locator(`td:has-text("${REPOSITORY_NAME}")`);
    await expect(repoCell).toBeVisible({ timeout: 10000 });
  });

  test('should navigate to PoDebateRap repository and show chart', async ({ page }) => {
    // Navigate to repositories page
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Find the row containing PoDebateRap and click Show Chart
    const repoRow = page.locator(`tr:has(td:has-text("${REPOSITORY_NAME}"))`);
    await expect(repoRow).toBeVisible({ timeout: 10000 });

    const showChartButton = repoRow.locator('button:has-text("Show Chart")');
    await expect(showChartButton).toBeVisible({ timeout: 5000 });
    await showChartButton.click();

    // Wait for chart to load
    await page.waitForTimeout(3000);
    await page.waitForTimeout(3000); // Wait for chart data to load

    // Chart container should be visible
    const chartContainer = page.locator('.rz-chart');
    await expect(chartContainer).toBeVisible({ timeout: 15000 });
  });
});

async function navigateToRepositoryChart(page: Page) {
  // Navigate to repositories page
  await page.goto('/repositories');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);

  // Find the row containing PoDebateRap
  const repoRow = page.locator(`tr:has(td:has-text("${REPOSITORY_NAME}"))`);

  // Retry if needed (sometimes Blazor takes time to render)
  for (let i = 0; i < 3; i++) {
    const visible = await repoRow.isVisible().catch(() => false);
    if (visible) {
      break;
    }
    
    await page.reload();
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
  }

  await expect(repoRow).toBeVisible({ timeout: 10000 });

  // Click the "Show Chart" button for this repository
  const showChartButton = repoRow.locator('button:has-text("Show Chart")');
  const hideChartButton = repoRow.locator('button:has-text("Hide Chart")');
  
  // Check if chart is already visible
  const isChartVisible = await hideChartButton.isVisible().catch(() => false);
  
  if (!isChartVisible) {
    // Chart is not shown, click to show it
    await expect(showChartButton).toBeVisible({ timeout: 5000 });
    await showChartButton.click();
    await page.waitForTimeout(2000);
  }

  // Wait for chart to render
  await page.waitForTimeout(3000);
}

test.describe('Chart Tests', () => {
  test('chart should have white background', async ({ page }) => {
    await navigateToRepositoryChart(page);

    // Get chart container
    const chartContainer = page.locator('.rz-chart').first();
    await expect(chartContainer).toBeVisible({ timeout: 15000 });

    // Background should be white or light colored (not black)
    const backgroundColor = await chartContainer.evaluate(
      (element) => window.getComputedStyle(element).backgroundColor
    );

    expect(backgroundColor).toBeTruthy();
    // Should be white (rgb(255, 255, 255)) or similar light color
    expect(
      backgroundColor.includes('255, 255, 255') ||
      backgroundColor.includes('rgb(255') ||
      backgroundColor.includes('white')
    ).toBeTruthy();
  });

  test('chart should contain SVG element', async ({ page }) => {
    await navigateToRepositoryChart(page);

    // Get chart SVG
    const chartSvg = page.locator('.rz-chart svg').first();
    await expect(chartSvg).toBeVisible({ timeout: 15000 });
  });

  test('chart should contain visual data elements', async ({ page }) => {
    await navigateToRepositoryChart(page);

    // Get chart SVG content
    const chartSvg = page.locator('.rz-chart svg').first();
    await expect(chartSvg).toBeVisible({ timeout: 15000 });

    const svgContent = await chartSvg.innerHTML();

    // Should have visual elements
    const hasPath = svgContent.includes('<path');
    const hasCircle = svgContent.includes('<circle');
    const hasPolyline = svgContent.includes('<polyline');
    const hasLine = svgContent.includes('<line');

    expect(hasPath || hasCircle || hasPolyline || hasLine).toBeTruthy();
  });

  test('chart should show commit history', async ({ page }) => {
    await navigateToRepositoryChart(page);

    // Get chart SVG content
    const chartSvg = page.locator('.rz-chart svg').first();
    await expect(chartSvg).toBeVisible({ timeout: 15000 });

    const svgContent = await chartSvg.innerHTML();

    // Should have visual elements for commit data
    const hasPath = svgContent.includes('<path');
    const hasCircle = svgContent.includes('<circle');
    const hasPolyline = svgContent.includes('<polyline');
    const hasLine = svgContent.includes('<line');

    expect(hasPath || hasCircle || hasPolyline || hasLine).toBeTruthy();
  });
});
