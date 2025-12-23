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

@secure()
@description('GitHub Personal Access Token for repository access')
param githubPAT string

// Log Analytics Workspace (required for Application Insights)
// This provides centralized log storage and querying capabilities
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'  // Pay-as-you-go tier (most cost-effective for small workloads)
    }
    retentionInDays: 30  // Minimum retention period (can be increased for compliance needs)
  }
}

// Application Insights for application telemetry and monitoring
// Workspace-based mode provides better integration with Log Analytics
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id  // Links to Log Analytics for log storage
    IngestionMode: 'LogAnalytics'  // Modern ingestion mode for better query capabilities
  }
}

// Reference existing Storage Account for Azure Table Storage
// Storage Account for Azure Table Storage
// Used to persist repository and commit line count data
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

// Table Service (part of Storage Account)
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Application-specific tables
resource repositoriesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerRepositories'
}

resource commitLineCountsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerCommitLineCounts'
}

resource failedOperationsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerFailedOperations'
}

// Azure Container Registry for storing container images
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

// Container App Environment for hosting the container app
// Uses Log Analytics workspace for log aggregation
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

// Container App for hosting the Blazor WebAssembly application and API
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  tags: {
    'azd-service-name': 'api'  // Azure Developer CLI service tag
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
        {
          name: 'storage-connection-string'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'github-pat'
          value: githubPAT
        }
        {
          name: 'appinsights-connection-string'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/api:latest'  // Will be replaced by azd deploy
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'appinsights-connection-string'
            }
            {
              name: 'AzureTableStorage__ConnectionString'
              secretRef: 'storage-connection-string'
            }
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
              name: 'GitHub__LocalReposPath'
              value: '/app/LocalRepos'  // Container path for temp storage
            }
            {
              name: 'GitHub__PAT'
              secretRef: 'github-pat'
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
