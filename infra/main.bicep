targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus2'

// Naming convention: Po{SolutionName} prefix. The live app + storage reside in the
// 'PoRepoLineTracker' resource group (not 'rg-...-prod'); keep Bicep aligned to reality
// so a provision does not create a duplicate, empty resource group.
var resourceGroupName = 'PoRepoLineTracker'
var sharedResourceGroupName = 'PoShared'
var storageAccountName = 'stporepolinetracker'  // Existing storage account in PoRepoLineTracker RG
var appInsightsName = 'poappideinsights8f9c9a4e'  // Shared App Insights in PoShared RG
var logAnalyticsName = 'PoShared-LogAnalytics'  // Shared Log Analytics in PoShared RG
var webAppName = 'app-porepolinetracker'  // App Service in PoRepoLineTracker RG
var appServicePlanName = 'asp-porepolinetracker'  // F1 (Free) Linux App Service Plan, created in PoRepoLineTracker RG
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
    // This subscription's Free (F1) App Service quota lives in West US 2 (East US 2 = 0),
    // so the F1 plan + app are created in West US 2. They still reside in the
    // PoRepoLineTracker RG; only the storage account stays in East US 2.
    webAppLocation: 'westus2'
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
