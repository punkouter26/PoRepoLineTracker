using './main.bicep'

// Deployment parameters for PoRepoLineTracker
// This file provides default values for deployment

param location = 'eastus'

// IMPORTANT: Replace these placeholders with actual values at deployment time
// Never commit real tokens to source control
// Deploy with: azd up (will prompt for secrets)
// Or: az deployment sub create --location eastus --template-file main.bicep --parameters @main.bicepparam --parameters githubPAT='your-token' --parameters githubClientId='your-client-id' --parameters githubClientSecret='your-client-secret'
param githubPAT = 'REPLACE_WITH_ACTUAL_TOKEN'
param githubClientId = 'REPLACE_WITH_ACTUAL_CLIENT_ID'
param githubClientSecret = 'REPLACE_WITH_ACTUAL_CLIENT_SECRET'
