targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus'

// Use the same name for everything based on solution name
var resourceGroupName = 'PoRepoLineTracker'
var storageAccountName = 'porepolinetracker'  // Storage account names must be lowercase, no special chars
var appInsightsName = 'PoRepoLineTracker'
var logAnalyticsName = 'PoRepoLineTracker'
var appServiceName = 'PoRepoLineTracker'

// Reference to shared resource group for App Service Plan
var sharedResourceGroupName = 'PoShared'
var sharedAppServicePlanName = 'PoShared'

// Create resource group
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
  }
}

// Outputs
output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_STORAGE_ACCOUNT_NAME string = resources.outputs.storageAccountName
output APPLICATIONINSIGHTS_CONNECTION_STRING string = resources.outputs.appInsightsConnectionString
output APP_SERVICE_URL string = resources.outputs.appServiceUrl
