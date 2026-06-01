---
name: azure-deployment-preflight
description: 'Performs comprehensive preflight validation of Bicep deployments to Azure, including template syntax validation, what-if analysis, and permission checks. Use this skill before any deployment to Azure to preview changes, identify potential issues, and ensure the deployment will succeed. Activate when users mention deploying to Azure, validating Bicep files, checking deployment permissions, previewing infrastructure changes, running what-if, or preparing for azd provision.'
---

# Azure Deployment Preflight Validation

This skill validates Bicep deployments before execution, supporting both Azure CLI (`az`) and Azure Developer CLI (`azd`) workflows.

## When to Use This Skill

- Before deploying infrastructure to Azure
- When preparing or reviewing Bicep files
- To preview what changes a deployment will make
- To verify permissions are sufficient for deployment
- Before running `azd up`, `azd provision`, or `az deployment` commands

## Validation Process

### Step 1: Detect Project Type

1. **Check for azd project**: Look for `azure.yaml` in the project root
   - If found → Use **azd workflow**
   - If not found → Use **az CLI workflow**

2. **Locate Bicep files**: Find all `.bicep` files to validate

3. **Auto-detect parameter files**: For each Bicep file, look for matching `.bicepparam` or `.parameters.json` files

### Step 2: Validate Bicep Syntax

Run Bicep CLI to check template syntax:
```bash
bicep build <bicep-file> --stdout
```

Capture syntax errors with line/column numbers and warning messages.

### Step 3: Run Preflight Validation

#### For azd Projects (azure.yaml exists)
```bash
azd provision --preview
```

#### For Standalone Bicep (no azure.yaml)

Determine the deployment scope from the Bicep file's `targetScope`:

| Target Scope | Command |
|--------------|---------|
| `resourceGroup` | `az deployment group what-if` |
| `subscription` | `az deployment sub what-if` |
| `managementGroup` | `az deployment mg what-if` |
| `tenant` | `az deployment tenant what-if` |

Run with `--validation-level Provider` first. If RBAC errors occur, fall back to `--validation-level ProviderNoRbac`.

### Step 4: Capture What-If Results

Parse the what-if output to categorize resource changes:

| Change Type | Symbol | Meaning |
|-------------|--------|---------|
| Create | `+` | New resource will be created |
| Delete | `-` | Resource will be deleted |
| Modify | `~` | Resource properties will change |
| NoChange | `=` | Resource unchanged |

### Step 5: Generate Report

Create a `preflight-report.md` in the project root with:
1. Summary — Overall status, timestamp, files validated
2. Tools Executed — Commands run, versions used
3. Issues — All errors and warnings with severity and remediation
4. What-If Results — Resources to create/modify/delete
5. Recommendations — Actionable next steps

## Error Handling

| Error Type | Action |
|------------|--------|
| Not logged in | Note in report, suggest `az login` |
| Permission denied | Fall back to `ProviderNoRbac` |
| Bicep syntax error | Include all errors, continue to other files |
| Tool not installed | Note in report, skip that step |
| Resource group not found | Note in report, suggest creating it |

## Tool Requirements

- **Azure CLI** (`az`) — Version 2.76.0+ recommended
- **Azure Developer CLI** (`azd`) — For projects with `azure.yaml`
- **Bicep CLI** (`bicep`) — For syntax validation
