using './main.bicep'

// Deployment parameters for PoRepoLineTracker
// Secrets are no longer passed as Bicep params — they are sourced from
// the PoShared Key Vault at runtime via DefaultAzureCredential.
// Deploy with: azd up

param location = 'eastus'
