using './main.bicep'

// Deployment parameters for PoRepoLineTracker
// This file provides default values for deployment

param location = 'eastus'

// IMPORTANT: Replace this placeholder with actual GitHub PAT at deployment time
// Never commit real tokens to source control
// Deploy with: az deployment sub create --location eastus --template-file main.bicep --parameters @main.bicepparam --parameters githubPAT='your-actual-token'
param githubPAT = 'REPLACE_WITH_ACTUAL_TOKEN'
