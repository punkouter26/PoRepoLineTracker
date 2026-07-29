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
// App Service Plan name — references the SHARED plan owned by PoShared RG (consolidation
// target — see ADR-031). infra/resources.bicep only REFERENCES the existing plan via
// an `existing` resource; it does NOT create, update, or delete it. The legacy in-RG
// plans (asp-porepolinetracker) still exist and still host the existing live site until
// a manual cross-stamp migration is performed (Azure blocks automated re-parenting
// across home stamps with extended error 59602).
var appServicePlanName = 'asp-PoShared-b1'
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
    // Plan + app run in West US 2 (where the plan was provisioned); the storage account
    // stays in East US 2. B1 is dedicated so region is not quota-constrained, but we keep
    // West US 2 to match the live resource and avoid a plan recreate.
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
