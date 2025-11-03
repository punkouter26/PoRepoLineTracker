import { test, expect } from '@playwright/test';

/**
 * Debugging test - Investigates why wrong repository is being added
 * This test documents the actual behavior vs expected behavior
 */
test.describe('Debug - Repository Selection Bug', () => {
  
  test('should investigate repository selection behavior', async ({ page }) => {
    // Navigate to add repository page
    await page.goto('/add-repository');
    await page.waitForLoadState('networkidle');

    // Wait for GitHub repositories to load
    await page.waitForTimeout(2000);
    await page.locator('label.repository-label').first().waitFor({ state: 'visible', timeout: 30000 });
    await page.waitForTimeout(2000);

    // Log all available repositories
    const repoLabels = page.locator('label.repository-label');
    const count = await repoLabels.count();
    console.log(`\n=== Found ${count} repositories ===`);
    
    for (let i = 0; i < count; i++) {
      const labelText = await repoLabels.nth(i).textContent();
      const checkbox = page.locator('input[type="checkbox"]').nth(i);
      const value = await checkbox.getAttribute('value');
      console.log(`Repository ${i}: Label="${labelText?.trim()}", Value="${value}"`);
    }

    // Find PoDebateRap
    const poDebateRapLabel = page.locator('label.repository-label:has-text("PoDebateRap")');
    const isVisible = await poDebateRapLabel.isVisible();
    
    if (!isVisible) {
      console.log('\n❌ PoDebateRap not found in repository list');
      console.log('Available repositories:');
      for (let i = 0; i < Math.min(count, 10); i++) {
        const labelText = await repoLabels.nth(i).textContent();
        console.log(`  - ${labelText?.trim()}`);
      }
      return;
    }

    console.log('\n✓ PoDebateRap found in repository list');

    // Get the checkbox associated with PoDebateRap
    const poDebateRapIndex = await page.locator('label.repository-label').evaluateAll((labels) => {
      return labels.findIndex(label => label.textContent?.includes('PoDebateRap'));
    });

    console.log(`PoDebateRap is at index: ${poDebateRapIndex}`);

    const poDebateRapCheckbox = page.locator('input[type="checkbox"]').nth(poDebateRapIndex);
    const checkboxValue = await poDebateRapCheckbox.getAttribute('value');
    const checkboxId = await poDebateRapCheckbox.getAttribute('id');
    
    console.log(`PoDebateRap checkbox - Value: "${checkboxValue}", ID: "${checkboxId}"`);

    // Click the label
    await poDebateRapLabel.click();
    await page.waitForTimeout(500);

    // Verify checkbox is checked
    const isChecked = await poDebateRapCheckbox.isChecked();
    console.log(`Checkbox checked after click: ${isChecked}`);

    // Check the selected repositories count
    const addButton = page.locator('button:has-text("Add Selected")');
    const buttonText = await addButton.textContent();
    console.log(`Add button text: "${buttonText}"`);

    // Take screenshot before clicking
    await page.screenshot({ path: 'test-results/debug-before-add.png', fullPage: true });

    // Intercept the API call to see what's being sent
    const [request] = await Promise.all([
      page.waitForRequest(request => request.url().includes('/api/repositories/bulk')),
      addButton.click()
    ]);

    const postData = request.postDataJSON();
    console.log('\n=== Data sent to API ===');
    console.log(JSON.stringify(postData, null, 2));

    // Wait for response
    await page.waitForSelector('text=/added successfully/i', { timeout: 120000 });
    await page.waitForTimeout(3000);

    // Check what was actually added
    await page.goto('/repositories');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    const tableRows = await page.locator('tbody tr').count();
    console.log(`\n=== Repositories in database after add ===`);
    
    for (let i = 0; i < tableRows; i++) {
      const ownerCell = page.locator('tbody tr').nth(i).locator('td').nth(0);
      const nameCell = page.locator('tbody tr').nth(i).locator('td').nth(1);
      
      const owner = await ownerCell.textContent();
      const name = await nameCell.textContent();
      
      console.log(`Repository ${i}: ${owner?.trim()}/${name?.trim()}`);
    }

    // Take final screenshot
    await page.screenshot({ path: 'test-results/debug-after-add.png', fullPage: true });
  });
});
