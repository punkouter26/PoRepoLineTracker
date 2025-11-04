# Secrets Management Guide

## Overview
This application uses **different secret storage methods** for local development vs. Azure deployment.

---

## 🔐 Local Development Secrets

### Storage Location
**Windows:** `C:\Users\{YourUsername}\AppData\Roaming\Microsoft\UserSecrets\a1b2c3d4-e5f6-7890-1234-567890abcdef\secrets.json`

**macOS/Linux:** `~/.microsoft/usersecrets/a1b2c3d4-e5f6-7890-1234-567890abcdef/secrets.json`

### Current Secrets Stored Locally
```json
{
  "GitHub:PAT": "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",
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=68eb72dd-74b6-4929-b5f8-41a188f94e22...",
  "AZURE_TENANT_ID": "5da66fe6-bd58-4517-8727-deebc8525dcb",
  "AZURE_LOCATION": "uksouth",
  "WEB_URI": "https://poredoimage.azurewebsites.net",
  "RESOURCE_GROUP_ID": "/subscriptions/f0504e26-451a-4249-8fb3-46270defdd5b/resourceGroups/PoRedoImage"
}
```

### How to Backup/Transfer Secrets

#### Option 1: Export to File (Manual Backup)
```powershell
# Export current secrets
$secretsPath = "$env:APPDATA\Microsoft\UserSecrets\a1b2c3d4-e5f6-7890-1234-567890abcdef\secrets.json"
Copy-Item $secretsPath -Destination ".\secrets-backup.json"

# Store this file in a SECURE location (OneDrive, password manager, encrypted USB)
# DO NOT commit to Git!
```

#### Option 2: Set on New Computer
```powershell
# On your new computer, run these commands:
cd PoRepoLineTracker
dotnet user-secrets set "GitHub:PAT" "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" --project src/PoRepoLineTracker.Api
dotnet user-secrets set "APPLICATIONINSIGHTS_CONNECTION_STRING" "InstrumentationKey=68eb72dd-74b6-4929-b5f8-41a188f94e22..." --project src/PoRepoLineTracker.Api
```

#### Option 3: Copy secrets.json Directly
```powershell
# On new computer:
$secretsDir = "$env:APPDATA\Microsoft\UserSecrets\a1b2c3d4-e5f6-7890-1234-567890abcdef"
New-Item -ItemType Directory -Path $secretsDir -Force
Copy-Item ".\secrets-backup.json" -Destination "$secretsDir\secrets.json"
```

---

## ☁️ Azure Production Secrets

### Storage Location
**Azure App Service → Configuration → Application Settings**

Current settings in Azure:
- `APPLICATIONINSIGHTS_CONNECTION_STRING` - Azure Application Insights telemetry
- `AzureTableStorage__ConnectionString` - Azure Table Storage connection
- `GitHub__PAT` - **⚠️ Currently set to "REPLACE_WITH_ACTUAL_TOKEN"**
- `GitHub__LocalReposPath` - Repository clone path

### How to Update Azure Secrets

#### Option 1: Azure Portal (GUI)
1. Go to https://portal.azure.com
2. Navigate to: Resource Groups → PoRepoLineTracker → PoRepoLineTracker (App Service)
3. Click: Settings → Configuration
4. Update `GitHub__PAT` value to your actual token
5. Click "Save" → "Continue"

#### Option 2: Azure CLI (Command Line)
```powershell
az webapp config appsettings set `
  --name PoRepoLineTracker `
  --resource-group PoRepoLineTracker `
  --settings GitHub__PAT="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"
```

#### Option 3: Azure Developer CLI (azd)
Azure secrets are automatically managed during `azd provision` if you:
1. Set the secret as a Bicep parameter
2. Provide it during provisioning

---

## 🔄 Secret Sync Strategy (Recommended)

### For Team/Multi-Computer Setup

**Use Azure Key Vault** (not currently configured but recommended):

1. **Store secrets in Azure Key Vault**
   - One source of truth
   - Accessible from any computer with Azure access
   - Automatic rotation support

2. **Reference from App Service**
   - App Service can reference Key Vault secrets directly
   - No need to duplicate secrets

3. **Local Development**
   - Use Azure CLI authentication
   - Pull secrets from Key Vault on demand

### Simple Multi-Computer Strategy (Current Setup)

**Keep a secure backup of `secrets.json`:**

```powershell
# Create encrypted backup (Windows)
$secretsPath = "$env:APPDATA\Microsoft\UserSecrets\a1b2c3d4-e5f6-7890-1234-567890abcdef\secrets.json"
Compress-Archive -Path $secretsPath -DestinationPath "secrets-backup.zip" -CompressionLevel Optimal

# Store in:
# - OneDrive/Google Drive (private folder)
# - Password manager (1Password, Bitwarden, etc.)
# - Encrypted USB drive
```

**Restore on new computer:**
```powershell
# Extract and copy to user secrets location
Expand-Archive -Path "secrets-backup.zip" -DestinationPath $env:TEMP
$secretsDir = "$env:APPDATA\Microsoft\UserSecrets\a1b2c3d4-e5f6-7890-1234-567890abcdef"
New-Item -ItemType Directory -Path $secretsDir -Force
Copy-Item "$env:TEMP\secrets.json" -Destination "$secretsDir\secrets.json"
```

---

## 🚨 Important Security Notes

1. **NEVER commit secrets to Git** ✅ Already configured:
   - User secrets stored outside repository
   - `.gitignore` excludes secrets files

2. **Azure App Service Setting `GitHub__PAT` needs updating**
   - Currently: "REPLACE_WITH_ACTUAL_TOKEN"
   - Should be: Your actual GitHub PAT

3. **GitHub Personal Access Token (PAT)**
   - Current token: `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`
   - Scope needed: `repo` (private repository access)
   - Regenerate if compromised

4. **UserSecretsId is safe to commit**
   - `a1b2c3d4-e5f6-7890-1234-567890abcdef` is just an ID
   - Actual secrets are stored separately

---

## 📋 Quick Commands Reference

### List all secrets
```powershell
dotnet user-secrets list --project src/PoRepoLineTracker.Api
```

### Add/Update a secret
```powershell
dotnet user-secrets set "GitHub:PAT" "your-token-here" --project src/PoRepoLineTracker.Api
```

### Remove a secret
```powershell
dotnet user-secrets remove "GitHub:PAT" --project src/PoRepoLineTracker.Api
```

### Clear all secrets
```powershell
dotnet user-secrets clear --project src/PoRepoLineTracker.Api
```

### View Azure App Service settings
```powershell
az webapp config appsettings list --name PoRepoLineTracker --resource-group PoRepoLineTracker
```

---

## ✅ Checklist for New Computer

- [ ] Clone repository: `git clone https://github.com/punkouter26/PoRepoLineTracker.git`
- [ ] Install .NET 9 SDK
- [ ] Restore secrets from backup OR set manually
- [ ] Verify secrets: `dotnet user-secrets list --project src/PoRepoLineTracker.Api`
- [ ] Test app locally: `dotnet run --project src/PoRepoLineTracker.Api`
- [ ] Verify Azure connection (if needed)

---

## 🔧 Troubleshooting

**Issue:** Secrets not loading in app
- Check UserSecretsId matches in `.csproj`: `a1b2c3d4-e5f6-7890-1234-567890abcdef`
- Verify secrets.json exists at correct path
- Ensure running in Development environment

**Issue:** Azure deployment can't access private repos
- Update `GitHub__PAT` in Azure App Service settings
- Restart App Service after updating

**Issue:** Lost secrets when switching computers
- Restore from backup (OneDrive/password manager)
- Or re-enter using `dotnet user-secrets set` commands
