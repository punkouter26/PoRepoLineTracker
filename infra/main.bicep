targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus'

// Naming convention: Po{SolutionName} prefix; resource group follows rg-Po{Name}-prod pattern
var resourceGroupName = 'PoRepoLineTracker'
var sharedResourceGroupName = 'PoShared'
var storageAccountName = 'porepolinetrackeraca'  // Existing storage account in PoRepoLineTracker RG
var appInsightsName = 'PoRepoLineTracker'
var logAnalyticsName = 'PoRepoLineTracker'
var containerAppName = 'porepolinetracker'
var containerAppEnvName = 'porepolinetracker-env'
var containerRegistryName = 'porepolinetrackeracr'
var keyVaultName = 'kv-poshared'  // Existing Key Vault in PoShared RG

// Reference the app resource group (must already exist or be created separately)
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
}

// Reference the PoShared resource group (must already exist — contains Key Vault, App Insights, etc.)
resource sharedRg 'Microsoft.Resources/resourceGroups@2021-04-01' existing = {
  name: sharedResourceGroupName
}

// Deploy resources into the app resource group
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
output CONTAINER_APP_URL string = resources.outputs.containerAppUrl

@description('Azure Container Registry endpoint')
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.containerRegistryEndpoint
