@description('Location for the Web App (must match the App Service Plan location)')
param webAppLocation string

@description('Name of the Azure Storage Account for Table Storage')
param storageAccountName string

@description('Name of the Application Insights instance')
param appInsightsName string

@description('Name of the Log Analytics Workspace')
param logAnalyticsName string

@description('Name of the App Service (Web App)')
param webAppName string

@description('Name of the shared Linux App Service Plan in PoShared RG')
param appServicePlanName string

@description('Name of the Key Vault in the PoShared resource group')
param keyVaultName string

@description('Name of the PoShared resource group containing shared services')
param sharedResourceGroupName string

// ═════════════════════════════════════════════════════════════════════════════════════════
// IMPORTANT: Shared App Service Plan in PoShared RG must be configured with:
//   - Linux OS
//   - SKU Tier: B1 (Basic - low-cost, suitable for non-production)
//             S1 (Standard - for production workloads)
//   - Minimum 1 instance
//
// This Bicep template references the EXISTING plan. To verify/update the plan SKU:
//   az appservice plan show --name asp-poshared-linux --resource-group PoShared
// ═════════════════════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────
// Existing resources (pre-created in this RG)
// ─────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource storageTableDataContributorRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  // Built-in role: Storage Table Data Contributor
  name: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  scope: subscription()
}

// Shared resources in PoShared RG
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsName
  scope: resourceGroup(sharedResourceGroupName)
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
  scope: resourceGroup(sharedResourceGroupName)
}

// ─────────────────────────────────────────────
// Reference Key Vault in PoShared resource group
// ─────────────────────────────────────────────

resource sharedKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
  scope: resourceGroup(sharedResourceGroupName)
}

// ─────────────────────────────────────────────
// Reference shared Linux App Service Plan in PoShared RG
// ─────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverFarms@2023-12-01' existing = {
  name: appServicePlanName
  scope: resourceGroup(sharedResourceGroupName)
}

// ─────────────────────────────────────────────
// App Service (Web App) — uses system-assigned managed identity
// Secrets pulled from PoShared Key Vault at runtime via DefaultAzureCredential
// Uses shared App Service Plan from PoShared RG (westus2)
// ─────────────────────────────────────────────

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: webAppLocation
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'api'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      // startup.sh is deployed in the code package and installs git before starting the app.
      // The workflow sets this AFTER code deployment to avoid a race condition where
      // provision restarts the app before startup.sh exists in wwwroot.
      // DO NOT rely on azd provision alone to set this — run the CI/CD pipeline.
      appCommandLine: 'dotnet PoRepoLineTracker.Api.dll'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        // Key Vault URI — app reads secrets via DefaultAzureCredential at startup.
        // MUST be 'KeyVault__Uri' to bind to config key 'KeyVault:Uri' that Program.cs reads.
        {
          name: 'KeyVault__Uri'
          value: sharedKeyVault.properties.vaultUri
        }
        // Explicit Production environment (App Service defaults to Production, but be explicit).
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        // Table Storage — use DefaultAzureCredential, not connection string keys
        {
          name: 'AzureTableStorage__ServiceUrl'
          value: storageAccount.properties.primaryEndpoints.table
        }
        // Non-secret table names
        {
          name: 'AzureTableStorage__RepositoryTableName'
          value: 'PoRepoLineTrackerRepositories'
        }
        {
          name: 'AzureTableStorage__CommitLineCountTableName'
          value: 'PoRepoLineTrackerCommitLineCounts'
        }
        {
          name: 'AzureTableStorage__FailedOperationTableName'
          value: 'PoRepoLineTrackerFailedOperations'
        }
        {
          name: 'AzureTableStorage__UserTableName'
          value: 'PoRepoLineTrackerUsers'
        }
        {
          name: 'AzureTableStorage__UserPreferencesTableName'
          value: 'PoRepoLineTrackerUserPreferences'
        }
        {
          name: 'GitHub__LocalReposPath'
          value: '/home/LocalRepos'
        }
        {
          name: 'GitHub__CallbackPath'
          value: '/signin-github'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://+:8080'
        }
      ]
    }
  }
}

// Storage Table Data Contributor role assignment
// GUID matches the existing assignment in Azure (idempotent: ARM re-uses same ID)
resource webAppStorageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: '671a4bb5-eb20-4862-beca-13ee459d991c'
  scope: storageAccount
  properties: {
    roleDefinitionId: storageTableDataContributorRole.id
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─────────────────────────────────────────────
// Access notes:
// - Storage Table data-plane access is managed here with Azure RBAC.
// - kv-poshared currently uses Key Vault access policy mode, not RBAC
//   (enableRbacAuthorization=false). Grant the Web App identity secret get/list
//   permissions on kv-poshared, or migrate the vault to RBAC mode and assign
//   Key Vault Secrets User at the vault scope.
// ─────────────────────────────────────────────

// Outputs
@description('Name of the deployed Storage Account')
output storageAccountName string = storageAccount.name

@description('Application Insights connection string for telemetry')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Public URL of the deployed Web App')
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'

@description('Resource ID of the Log Analytics Workspace')
output logAnalyticsId string = logAnalytics.id

@description('Web App managed identity principal ID (use to grant Key Vault access)')
output webAppPrincipalId string = webApp.identity.principalId
