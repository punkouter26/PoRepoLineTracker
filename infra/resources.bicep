@description('Azure region where resources will be deployed')
param location string

@description('Name of the Azure Storage Account for Table Storage')
param storageAccountName string

@description('Name of the Application Insights instance')
param appInsightsName string

@description('Name of the Log Analytics Workspace')
param logAnalyticsName string

@description('Name of the Container App')
param containerAppName string

@description('Name of the Container App Environment')
param containerAppEnvName string

@description('Name of the Azure Container Registry')
param containerRegistryName string

@description('Name of the Key Vault in the PoShared resource group')
param keyVaultName string

@description('Name of the PoShared resource group containing shared services')
param sharedResourceGroupName string

// ─────────────────────────────────────────────
// Existing resources (pre-created in this RG)
// ─────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
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
// Container Registry (Basic SKU — lowest cost)
// ─────────────────────────────────────────────

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// ─────────────────────────────────────────────
// Container App Environment
// ─────────────────────────────────────────────

resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: containerAppEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    daprAIConnectionString: appInsights.properties.ConnectionString
    zoneRedundant: false
  }
}

// ─────────────────────────────────────────────
// Container App — uses system-assigned managed identity
// Secrets pulled from PoShared Key Vault at runtime
// ─────────────────────────────────────────────

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'api'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          username: containerRegistry.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: containerRegistry.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/api:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
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
              value: '/app/LocalRepos'
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
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

// ─────────────────────────────────────────────
// RBAC: Grant Container App managed identity access to Table Storage
// Role: Storage Table Data Contributor (0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3)
// ─────────────────────────────────────────────

resource storageTableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, containerApp.id, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  scope: storageAccount
  properties: {
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  }
}

// ─────────────────────────────────────────────
// RBAC: Grant Container App managed identity access to PoShared Key Vault
// Role: Key Vault Secrets User (4633458b-17de-408a-b874-0445c86b69e6)
// This must be applied in the PoShared RG. Configure via Azure portal or az CLI:
//   az role assignment create --role "Key Vault Secrets User" \
//     --assignee <containerApp-principalId> \
//     --scope /subscriptions/.../resourceGroups/PoShared/providers/Microsoft.KeyVault/vaults/kv-poshared
// ─────────────────────────────────────────────

// Outputs
@description('Name of the deployed Storage Account')
output storageAccountName string = storageAccount.name

@description('Application Insights connection string for telemetry')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Public URL of the deployed Container App')
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

@description('Resource ID of the Log Analytics Workspace')
output logAnalyticsId string = logAnalytics.id

@description('Azure Container Registry login server')
output containerRegistryEndpoint string = containerRegistry.properties.loginServer

@description('Container App managed identity principal ID (use to grant Key Vault access)')
output containerAppPrincipalId string = containerApp.identity.principalId
