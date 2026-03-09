# DevOps Reference — PoRepoLineTracker

## 1. Technology Stack

| Layer | Technology |
|-------|-----------|
| API + Hosting | .NET 10 (ASP.NET Core), Linux App Service (B1/S1) |
| Frontend | Blazor WASM hosted inside same App Service |
| IaC | Azure Developer CLI (`azd`) + Bicep |
| CI/CD | GitHub Actions → `azd up` |
| Auth | OIDC federated credentials (no stored secret/PAT) |
| Local dev | Docker Compose (Azurite) + `dotnet run` |

---

## 2. Azure Infrastructure

All infrastructure is defined in `infra/resources.bicep` and `infra/main.bicep`.

### Resources per Deployment

| Resource | SKU | Notes |
|----------|-----|-------|
| App Service (Linux) | B1 / configurable | Same RG as storage |
| App Service Plan | asp-poshared-linux | **Shared** — lives in `PoShared` RG |
| Azure Storage Account | Standard LRS | Existing resource; `allowBlobPublicAccess: false` |
| Azure Application Insights | Workspace-based | **Shared** — lives in `PoShared` RG |
| Log Analytics Workspace | Per GB pay-as-you-go | **Shared** — lives in `PoShared` RG |
| Azure Key Vault | Standard | **Shared** — lives in `PoShared` RG |

### App Service Configuration
- Runtime: `DOTNETCORE|10.0`
- HTTPS only: `true`, minimum TLS 1.2
- Managed Identity: `SystemAssigned`
- Health probe: `GET /health`

### Key Vault Secrets Required

| Secret Name | Description |
|-------------|-------------|
| `GitHub--ClientId` | GitHub OAuth App client ID |
| `GitHub--ClientSecret` | GitHub OAuth App client secret |
| `GitHub--PAT` | Personal Access Token for cloning private repos |

Secrets are loaded at startup via `PrefixKeyVaultSecretManager` using `DefaultAzureCredential` (Managed Identity in production, developer account locally).

---

## 3. Initial Azure Setup (Day 1)

### Prerequisites
```bash
# Install Azure Developer CLI
winget install Microsoft.Azd

# Install Azure CLI
winget install Microsoft.AzureCLI

# Login
az login
azd auth login
```

### First Deployment
```bash
cd c:\path\to\PoRepoLineTracker

# Initialise environment (once)
azd env new <env-name>        # e.g. "prod" or "dev"
azd env set AZURE_LOCATION eastus
azd env set AZURE_SUBSCRIPTION_ID <your-sub-id>

# Provision infrastructure + deploy application
azd up
```

`azd up` runs `azd provision` (Bicep) then `azd deploy` (dotnet publish → zip deploy).

### Key Vault Secrets (post-provision)
```bash
# Replace <kv-name> with the Key Vault name from azd output
az keyvault secret set --vault-name <kv-name> --name "GitHub--ClientId"     --value "<github-oauth-client-id>"
az keyvault secret set --vault-name <kv-name> --name "GitHub--ClientSecret" --value "<github-oauth-client-secret>"
az keyvault secret set --vault-name <kv-name> --name "GitHub--PAT"          --value "<github-pat>"
```

### GitHub OAuth App Registration
1. Go to **GitHub → Settings → Developer settings → OAuth Apps → New OAuth App**.
2. Set **Authorization callback URL** to `https://<your-app-service-url>/signin-github`.
3. Copy the **Client ID** and generate a **Client Secret** → store in Key Vault (above).

---

## 4. CI/CD Pipeline

### Workflow File
`.github/workflows/azure-dev.yml`

### Trigger
- Push to `master` branch → automatic deploy.
- Manual: `workflow_dispatch`.

### Pipeline Steps
```
checkout → azd up --no-prompt → done
```

`azd up` handles both infrastructure provisioning (idempotent Bicep) and application deployment in a single command.

### Required GitHub Repository Variables

> Variables (not secrets) — set in **Repository Settings → Secrets and variables → Actions → Variables tab**.

| Variable | Value |
|----------|-------|
| `AZURE_CLIENT_ID` | Entra App Registration client ID for OIDC |
| `AZURE_TENANT_ID` | Azure tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_ENV_NAME` | `azd` environment name (e.g. `prod`) |
| `AZURE_LOCATION` | Azure region (e.g. `eastus`) |

### OIDC Federated Credentials Setup (one-time)
```bash
# Create an App Registration, add federated credentials for GitHub Actions
az ad app create --display-name "PoRepoLineTracker-CICD"
# Then add federated credential for the repo + branch in Azure Portal:
# Azure AD → App Registrations → <app> → Certificates & secrets → Federated credentials
# Issuer: https://token.actions.githubusercontent.com
# Subject: repo:<github-org/repo>:ref:refs/heads/master
```

**No client secret is stored anywhere** — OIDC token exchange is used exclusively.

---

## 5. Local Development

### Prerequisites
- .NET 10 SDK
- Docker Desktop (for Azurite)
- Azure CLI (for Key Vault access, optional)

### Start Azurite (Azure Table Storage emulator)
```bash
cd c:\path\to\PoRepoLineTracker
docker compose up -d
# Azurite exposes: Blob 10000, Queue 10001, Table 10002
```

### Run API + Client
```bash
dotnet run --project src/PoRepoLineTracker.Api
# App available at: http://localhost:5000
# Health check:     http://localhost:5000/health
```

`appsettings.Development.json` points `AzureWebJobsStorage` / Table Storage connection to `UseDevelopmentStorage=true` (Azurite).

For GitHub OAuth to work in dev, register a **separate** GitHub OAuth App with callback:
`http://localhost:5000/signin-github`

### Dev Login Bypass
When `ASPNETCORE_ENVIRONMENT=Development`, a test endpoint is available:
```
GET http://localhost:5000/test-login-redirect?email=<your-email>
```
This sets an auth cookie and redirects to `/` without a real GitHub OAuth round-trip.
> **Requires**: Azurite running (user record is written to Table Storage).

---

## 6. Build & Test

### Build
```bash
dotnet build PoRepoLineTracker.sln
```

### Unit Tests
```bash
dotnet test tests/PoRepoLineTracker.UnitTests
```

### Integration Tests
```bash
# Requires Azurite running
docker compose up -d
dotnet test tests/PoRepoLineTracker.IntegrationTests
```

### E2E Tests (Playwright / TypeScript)
```bash
cd tests/PoRepoLineTracker.E2ETests.TS
npm install
npx playwright test
# Reports in: playwright-report/
```

---

## 7. Observability

### Structured Logging
- **Serilog** sinks: Console + rolling file (`log<date>.txt`) + Application Insights Serilog sink.
- Log level controlled via `appsettings.json` → `Serilog:MinimumLevel`.

### OpenTelemetry Traces
- Exported to Application Insights via `AddAzureMonitorTraceExporter`.
- Traces include HTTP request spans, MediatR handler spans, and Table Storage calls.

### Key Application Insights Queries
```kusto
-- All errors in the last 24 hours
exceptions
| where timestamp > ago(24h)
| project timestamp, outerMessage, severityLevel, operation_Name
| order by timestamp desc

-- Request latency p95
requests
| where timestamp > ago(7d)
| summarize percentile(duration, 95) by bin(timestamp, 1h)
```

---

## 8. Security Notes

- **Auth cookie**: HttpOnly, Secure, SameSite=Lax, 7-day sliding expiration.
- **Managed Identity**: App Service uses SystemAssigned identity to access Key Vault — no credentials stored in app config.
- **HTTPS only**: Enforced at App Service level + HSTS in middleware.
- **Secrets**: Never in source control — all in Key Vault.
- **GitHub PAT scope**: Minimum required — `repo` (read) for private repos, or `public_repo` for public only.
- **Table Storage**: `allowBlobPublicAccess: false`; access via connection string from Key Vault.
