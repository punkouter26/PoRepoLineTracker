# GitHub OAuth Setup Guide

This guide explains how to configure GitHub OAuth authentication for PoRepoLineTracker.

## Prerequisites

- A GitHub account
- Access to the PoRepoLineTracker application settings

## Step 1: Create a GitHub OAuth Application

1. Go to [GitHub Developer Settings](https://github.com/settings/developers)
2. Click **"New OAuth App"** (or "Register a new application")
3. Fill in the application details:

   | Field | Value |
   |-------|-------|
   | **Application name** | PoRepoLineTracker (or your preferred name) |
   | **Homepage URL** | `https://localhost:5001` (development) or your production URL |
   | **Application description** | (Optional) Repository line count tracking application |
   | **Authorization callback URL** | `https://localhost:5001/signin-github` |

4. Click **"Register application"**

## Step 2: Get Your Credentials

After creating the OAuth App, you'll see:

- **Client ID**: A public identifier (e.g., `Iv1.abc123def456`)
- **Client Secret**: Click "Generate a new client secret" to create one

⚠️ **Important**: Copy the Client Secret immediately - you won't be able to see it again!

## Step 3: Configure the Application

### Option A: Using User Secrets (Recommended for Development)

```bash
cd src/PoRepoLineTracker.Api
dotnet user-secrets set "GitHub:ClientId" "your-client-id"
dotnet user-secrets set "GitHub:ClientSecret" "your-client-secret"
```

### Option B: Using appsettings.Development.json (Not recommended)

Edit `src/PoRepoLineTracker.Api/appsettings.Development.json`:

```json
{
  "GitHub": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "CallbackPath": "/signin-github",
    "LocalReposPath": "C:\\LocalRepos"
  }
}
```

⚠️ **Warning**: Never commit secrets to source control!

### Option C: Using Environment Variables (Production)

```bash
# Windows PowerShell
$env:GitHub__ClientId = "your-client-id"
$env:GitHub__ClientSecret = "your-client-secret"

# Linux/macOS
export GitHub__ClientId="your-client-id"
export GitHub__ClientSecret="your-client-secret"
```

## Step 4: Production Configuration

For production deployment on Azure:

1. **Azure Key Vault** (Recommended):
   - Store secrets in Azure Key Vault
   - Use Managed Identity for access
   - Reference secrets in your application

2. **App Service Configuration**:
   - Add `GitHub__ClientId` and `GitHub__ClientSecret` as application settings
   - Mark the Client Secret as a "Key Vault Reference" or "Secret"

3. **Update Callback URL**:
   - In your GitHub OAuth App settings, update the callback URL to your production domain:
     `https://your-app-name.azurewebsites.net/signin-github`

## OAuth Scopes

The application requests the following GitHub OAuth scopes:

| Scope | Purpose |
|-------|---------|
| `user:email` | Access user's email addresses |
| `read:user` | Read user's profile information |
| `repo` | Access private repositories for line counting |

## Testing the Authentication

1. Start the application:
   ```bash
   dotnet run --project src/PoRepoLineTracker.AppHost
   ```

2. Open the Blazor client in your browser

3. Click **"Sign in with GitHub"** in the header

4. Authorize the application when prompted

5. You should be redirected back and see your GitHub profile information

## Troubleshooting

### "Invalid client_id" Error
- Verify the Client ID is correctly configured
- Check for extra whitespace or invalid characters

### "Redirect URI mismatch" Error
- Ensure the callback URL in GitHub matches exactly: `/signin-github`
- Check the scheme (http vs https) matches

### "Authorization callback URL not configured"
- Make sure `CallbackPath` is set to `/signin-github` in appsettings

### User Not Persisted
- Verify Azure Table Storage is running (Azurite for development)
- Check the `UserTableName` configuration

## Security Considerations

1. **Never commit secrets** to source control
2. **Rotate secrets** periodically
3. **Use HTTPS** in production
4. **Validate state parameter** (handled automatically by the OAuth library)
5. **Store access tokens securely** (stored encrypted in Azure Table Storage)

## Multi-User Architecture

With OAuth enabled:

- Each user authenticates with their own GitHub account
- Repositories are partitioned by user ID
- Each user sees only their own tracked repositories
- Access tokens are stored per-user and used for private repository access
