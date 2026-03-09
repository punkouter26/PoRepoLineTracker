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

@description('SKU tier for the App Service Plan (B1, B2, B3, or S1 for production)')
param appServicePlanSkuTier string = 'B1'

@description('SKU name for the App Service Plan (Basic or Standard)')
param appServicePlanSkuName string = 'B1'

@description('Name of the Azure Container Registry (globally unique, alphanumeric, 5-50 chars)')
param containerRegistryName string

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

// ─────────────────────────────────────────────
// Azure Container Registry — stores Docker images built from the Dockerfile
// App Service pulls images using its system-assigned managed identity (AcrPull role below)
// ─────────────────────────────────────────────

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: containerRegistryName
  location: webAppLocation
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false  // Managed identity auth — no admin credentials needed
  }
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
      // Docker image built from src/PoRepoLineTracker.Api/Dockerfile (includes git)
      // azd deploy pushes the image; :latest tag is updated on each deploy
      linuxFxVersion: 'DOCKER|${containerRegistry.properties.loginServer}/api:latest'
      acrUseManagedIdentityCreds: true  // Use system-assigned managed identity to pull from ACR
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        // Key Vault URL — app reads secrets via DefaultAzureCredential at startup
        {
          name: 'KeyVault__Url'
          value: sharedKeyVault.properties.vaultUri
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
        {
          name: 'DOCKER_REGISTRY_SERVER_URL'
          value: 'https://${containerRegistry.properties.loginServer}'
        }
      ]
    }
  }
}

// ─────────────────────────────────────────────
// AcrPull role: lets the App Service managed identity pull images from ACR
// Role definition ID 7f951dda-4ed3-4680-a7ca-43fe172d538d = AcrPull (built-in)
// ─────────────────────────────────────────────

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, webApp.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─────────────────────────────────────────────
// RBAC: Managed externally (already assigned)
// Web App managed identity has Storage Table Data Contributor on the storage account
// and Key Vault Secrets User on kv-poshared in PoShared RG.
// If the identity is recreated, re-run:
//   az role assignment create --role "Storage Table Data Contributor" \
//     --assignee <webApp-principalId> \
//     --scope /subscriptions/.../resourceGroups/PoRepoLineTracker/providers/Microsoft.Storage/storageAccounts/stporepolinetracker
//   az role assignment create --role "Key Vault Secrets User" \
//     --assignee <webApp-principalId> \
//     --scope /subscriptions/.../resourceGroups/PoShared/providers/Microsoft.KeyVault/vaults/kv-poshared
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

@description('Azure Container Registry login server (e.g. crporepo12345.azurecr.io)')
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
