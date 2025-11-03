targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus'

@secure()
@description('GitHub Personal Access Token for repository access')
param githubPAT string

// Naming convention: All resources use 'PoRepoLineTracker' prefix for consistency
// Storage account names must be lowercase with no special characters due to Azure constraints
var resourceGroupName = 'PoRepoLineTracker'
var storageAccountName = 'porepolinetracker'  // Lowercase, no hyphens
var appInsightsName = 'PoRepoLineTracker'
var logAnalyticsName = 'PoRepoLineTracker'
var appServiceName = 'PoRepoLineTracker'

// Shared infrastructure references
// Uses existing shared App Service Plan to minimize costs (Free F1 tier supports multiple apps)
var sharedResourceGroupName = 'PoShared'
var sharedAppServicePlanName = 'PoShared'

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
    appServiceName: appServiceName
    sharedAppServicePlanResourceId: '/subscriptions/${subscription().subscriptionId}/resourceGroups/${sharedResourceGroupName}/providers/Microsoft.Web/serverfarms/${sharedAppServicePlanName}'
    githubPAT: githubPAT
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
output APP_SERVICE_URL string = resources.outputs.appServiceUrl
