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

@description('Name of the App Service Plan that the web app binds to. Lives in the PoShared RG as a SHARED plan (consolidation target — see ADR-031). infra/resources.bicep only REFERENCES this plan via an `existing` resource; it does NOT create it.')
param appServicePlanName string

@description('Name of the Key Vault in the PoShared resource group')
param keyVaultName string

@description('Name of the PoShared resource group containing shared services and the App Service Plan')
param sharedResourceGroupName string

// ═════════════════════════════════════════════════════════════════════════════════════════
// App Service Plan (B1 Basic, Linux) is a SHARED resource in the PoShared RG; this template
// only references it. The App Service is created in THIS resource group (PoRepoLineTracker),
// alongside the storage account — see the resources below. App Insights, Key Vault, and Log
// Analytics remain in the shared PoShared RG and are referenced as existing. The legacy
// in-RG plan (asp-porepolinetracker) still hosts the existing live site; automated
// re-parenting is blocked by Azure's home-stamp affinity (extended error 59602 on
// cross-stamp serverFarmId patch).
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
// App Service Plan (existing, shared) — B1 (Basic), Linux. Lives in the PoShared RG.
// This is an `existing` reference; the plan is NOT created here. Cross-RG reference is
// required because the consolidation target lives in PoShared.
// ─────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: appServicePlanName
  scope: resourceGroup(sharedResourceGroupName)
}

// ─────────────────────────────────────────────
// App Service (Web App) — uses system-assigned managed identity
// Secrets pulled from PoShared Key Vault at runtime via DefaultAzureCredential
// Runs on the F1 (Free) Linux plan created above, in this resource group
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
      // B1 supports Always On — keep the app warm (avoids cold-start 5xx on first hit).
      alwaysOn: true
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

// Storage Table Data Contributor role assignment.
// Name is derived from (scope, principal, role) so it is stable across re-runs but changes
// when the web app's managed-identity principal changes — avoiding RoleAssignmentUpdateNotPermitted
// when a fresh App Service gets a new principalId.
resource webAppStorageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, webApp.id, storageTableDataContributorRole.id)
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

// ─────────────────────────────────────────────
// Availability test — ping /health every 5 min from 3 US regions and report to the
// shared App Insights, so an outage (like the recent home-page 5xx) is caught
// proactively instead of via user reports. Deployed to PoShared (same RG + region as
// the App Insights component, which the platform requires). Wire an action group later.
// ─────────────────────────────────────────────

module healthAvailabilityTest 'availability-test.bicep' = {
  name: 'availability-test'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    appInsightsId: appInsights.id
    location: appInsights.location
    webAppHostName: webApp.properties.defaultHostName
    webAppName: webAppName
  }
}

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
