@description('Azure region where resources will be deployed')
param location string

@description('Name of the Azure Storage Account for Table Storage')
param storageAccountName string

@description('Name of the Application Insights instance')
param appInsightsName string

@description('Name of the Log Analytics Workspace')
param logAnalyticsName string

@description('Name of the App Service')
param appServiceName string

@description('Resource ID of the shared App Service Plan')
param sharedAppServicePlanResourceId string

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

// Storage Account for Azure Table Storage
// Used to persist repository and commit line count data
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'  // Locally-redundant storage (3 copies in same datacenter, cost-effective)
  }
  kind: 'StorageV2'  // General-purpose v2 account with latest features
  properties: {
    minimumTlsVersion: 'TLS1_2'  // Enforce secure connections only
    allowBlobPublicAccess: false  // Disable public blob access for security
    supportsHttpsTrafficOnly: true  // Require HTTPS for all connections
    accessTier: 'Hot'  // Optimize for frequent access patterns
  }
}

// Diagnostic settings for Storage Account
resource storageAccountDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'storage-diagnostics'
  scope: storageAccount
  properties: {
    workspaceId: logAnalytics.id
    metrics: [
      {
        category: 'Transaction'
        enabled: true
      }
    ]
  }
}

// Table Service (part of Storage Account)
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storageAccount  // Uses parent property instead of / in name (Bicep best practice)
  name: 'default'
}

// Application-specific tables
// Table for storing repository metadata (owner, name, URL, etc.)
resource repositoriesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerRepositories'
}

// Table for storing commit line count history
resource commitLineCountsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerCommitLineCounts'
}

// Table for tracking failed operations for retry/debugging
resource failedOperationsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: 'PoRepoLineTrackerFailedOperations'
}

// App Service for hosting the Blazor WebAssembly application and API
// Uses a shared App Service Plan to minimize costs
resource appService 'Microsoft.Web/sites@2022-09-01' = {
  name: appServiceName
  location: 'eastus2'  // Must match shared plan location
  kind: 'app'
  tags: {
    'azd-service-name': 'api'  // Azure Developer CLI service tag
  }
  properties: {
    serverFarmId: sharedAppServicePlanResourceId  // References shared App Service Plan
    siteConfig: {
      netFrameworkVersion: 'v9.0'  // .NET 9 runtime
      use32BitWorkerProcess: true  // Required for Free/Shared tier plans
      alwaysOn: false  // Not available in Free tier (app may sleep after idle)
      ftpsState: 'Disabled'  // Disable FTP/FTPS for security
      minTlsVersion: '1.2'  // Minimum TLS version for security compliance
      healthCheckPath: '/api/health'  // Health check endpoint for monitoring
      appSettings: [
        // Application Insights configuration for telemetry
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'  // Latest major version of App Insights agent
        }
        // Azure Table Storage configuration
        {
          name: 'AzureTableStorage__ConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
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
        // GitHub integration configuration
        {
          name: 'GitHub__LocalReposPath'
          value: 'D:\\home\\site\\wwwroot\\LocalRepos'  // Azure App Service path for temp storage
        }
        {
          name: 'GitHub__PAT'
          value: githubPAT  // Secure parameter passed at deployment time
        }
      ]
    }
    httpsOnly: true
  }
}

// Diagnostic settings for App Service
resource appServiceDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'appservice-diagnostics'
  scope: appService
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceAppLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// Outputs
@description('Name of the deployed Storage Account')
output storageAccountName string = storageAccount.name

@description('Application Insights connection string for telemetry')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Public URL of the deployed App Service')
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'

@description('Resource ID of the Log Analytics Workspace')
output logAnalyticsId string = logAnalytics.id
