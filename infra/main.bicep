targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus2'

// Naming convention: Po{SolutionName} prefix; resource group follows rg-Po{Name}-prod pattern
var resourceGroupName = 'PoRepoLineTracker'
var sharedResourceGroupName = 'PoShared'
var storageAccountName = 'stporepolinetracker'  // Existing storage account in PoRepoLineTracker RG
var appInsightsName = 'poappideinsights8f9c9a4e'  // Shared App Insights in PoShared RG
var logAnalyticsName = 'PoShared-LogAnalytics'  // Shared Log Analytics in PoShared RG
var webAppName = 'app-porepolinetracker'  // App Service in PoRepoLineTracker RG
var appServicePlanName = 'asp-poshared-linux'  // Shared Linux App Service Plan in PoShared RG
var keyVaultName = 'kv-poshared'  // Existing Key Vault in PoShared RG

// Reference the app resource group (must already exist or be created separately)
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
}

// Deploy resources into the app resource group
module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    webAppLocation: 'westus2'  // Must match App Service Plan location in PoShared RG
    storageAccountName: storageAccountName
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    webAppName: webAppName
    appServicePlanName: appServicePlanName
    keyVaultName: keyVaultName
    sharedResourceGroupName: sharedResourceGroupName
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
output SERVICE_API_URL string = resources.outputs.webAppUrl
