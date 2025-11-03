# Known Issues with E2E Tests

## Issue: Wrong Repository Being Added

### Description
When selecting PoDebateRap repository and clicking "Add Selected", the system shows a success message but adds a different repository (PoBabyTouch) instead.

### Evidence
- Test: `setup-podebaterap.spec.ts - should add PoDebateRap repository from GitHub`
- Expected: PoDebateRap should be added
- Actual: PoBabyTouch is added
- Success message appears: "repositories added successfully"

### Investigation Needed
1. Check the bulk repository add endpoint `/api/repositories/bulk`
2. Verify the checkbox selection is correctly capturing the selected repository
3. Confirm the DTO being sent to the API contains the correct repository information

### Workaround
For now, E2E tests will need to work with whatever repository is actually added, or we need to fix the repository selection bug before E2E tests can reliably add specific repositories.

### Test Output
```
Row 0: punkouter26
                    PoBabyTouch
                    Never
                    ✓ Analyzed
                    Show Chart
```

The test clicked on the PoDebateRap label, clicked "Add Selected", saw the success message, but PoBabyTouch appeared in the table instead.

---

## Recommendation
Before continuing with E2E test development, the repository selection bug should be investigated and fixed.
