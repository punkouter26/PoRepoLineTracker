using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace PoRepoLineTracker.Api;

/// <summary>
/// Custom Key Vault secret manager that loads secrets for this application.
/// 
/// Key Vault naming convention (using "--" as hierarchy separator):
///   App-specific secrets are prefixed:  PoRepoLineTracker--GitHub--ClientId  → GitHub:ClientId
///   Shared secrets have no prefix:      APPLICATIONINSIGHTS-CONNECTION-STRING → APPLICATIONINSIGHTS_CONNECTION_STRING
/// 
/// This manager loads ALL secrets from the vault and strips the app prefix when present,
/// so consumers can reference config keys naturally (e.g., "GitHub:ClientId").
/// </summary>
public class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private const string AppPrefix = "PoRepoLineTracker--";

    /// <summary>
    /// Load all secrets — both prefixed (app-specific) and non-prefixed (shared).
    /// </summary>
    public override bool Load(SecretProperties properties)
    {
        return properties.Enabled == true;
    }

    /// <summary>
    /// Map Key Vault secret name to .NET configuration key.
    /// Strips the app prefix if present, and replaces "--" with ":" for hierarchy.
    /// </summary>
    public override string GetKey(KeyVaultSecret secret)
    {
        var name = secret.Name;

        // Strip app-specific prefix if present
        if (name.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[AppPrefix.Length..];
        }

        // Replace "--" with ":" for .NET configuration hierarchy
        return name.Replace("--", ":");
    }
}
