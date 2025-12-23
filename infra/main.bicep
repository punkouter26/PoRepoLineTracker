targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus'

@secure()
@description('GitHub Personal Access Token for repository access')
param githubPAT string

@secure()
@description('GitHub OAuth Client ID for authentication')
param githubClientId string

@secure()
@description('GitHub OAuth Client Secret for authentication')
param githubClientSecret string

// Naming convention: All resources use 'PoRepoLineTracker' prefix for consistency
// Storage account names must be lowercase with no special characters due to Azure constraints
var resourceGroupName = 'PoRepoLineTracker'
var storageAccountName = 'porepolinetracker'  // Use existing storage account
var appInsightsName = 'PoRepoLineTracker'  // Use existing App Insights
var logAnalyticsName = 'PoRepoLineTracker'  // Use existing Log Analytics
var containerAppName = 'porepolinetracker'  // Container App names must be lowercase
var containerAppEnvName = 'porepolinetracker-env'
var containerRegistryName = 'porepolinetracker25cr'

// Create resource group for this application's resources
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
}

// Deploy resources into the resource group
module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    storageAccountName: storageAccountName
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    containerAppName: containerAppName
    containerAppEnvName: containerAppEnvName
    containerRegistryName: containerRegistryName
    githubPAT: githubPAT
    githubClientId: githubClientId
    githubClientSecret: githubClientSecret
  }
}

// Outputs
@description('Azure region where resources were deployed')
output AZURE_LOCATION string = location

@description('Name of the resource group containing all resources')
output AZURE_RESOURCE_GROUP string = rg.name

@description('Name of the Azure Storage Account')
output AZURE_STORAGE_ACCOUNT_NAME string = resources.outputs.storageAccountName

@description('Application Insights connection string for telemetry configuration')
output APPLICATIONINSIGHTS_CONNECTION_STRING string = resources.outputs.appInsightsConnectionString

@description('Public URL of the deployed application')
output CONTAINER_APP_URL string = resources.outputs.containerAppUrl

@description('Azure Container Registry endpoint')
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.containerRegistryEndpoint
