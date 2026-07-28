namespace PoRepoLineTracker.Shared.Models;

/// <summary>
/// Every configuration key the application reads, in one place (Rule 1.5 — zero magic strings).
///
/// Before this existed the same key was spelled out at up to four call sites, so a rename meant
/// finding all of them and a typo meant a silently null value rather than a build error. Grouped
/// by configuration section to mirror appsettings.json, Key Vault (<c>PoRepoLineTracker--</c>
/// prefix), and App Service application settings.
/// </summary>
public static class ConfigKeys
{
    /// <summary>Azure Key Vault.</summary>
    public static class KeyVault
    {
        public const string Uri = "KeyVault:Uri";
    }

    /// <summary>Azure Table Storage connection and table names.</summary>
    public static class AzureTableStorage
    {
        public const string ConnectionString = "AzureTableStorage:ConnectionString";
        public const string ServiceUrl = "AzureTableStorage:ServiceUrl";
        public const string RepositoryTableName = "AzureTableStorage:RepositoryTableName";
        public const string CommitLineCountTableName = "AzureTableStorage:CommitLineCountTableName";
        public const string TopFilesTableName = "AzureTableStorage:TopFilesTableName";
        public const string UserTableName = "AzureTableStorage:UserTableName";
        public const string UserPreferencesTableName = "AzureTableStorage:UserPreferencesTableName";

        /// <summary>Aspire/.NET-style connection string, used as a fallback for the above.</summary>
        public const string TablesConnectionString = "ConnectionStrings:tables";
    }

    /// <summary>GitHub OAuth credentials and API access.</summary>
    public static class GitHub
    {
        public const string ClientId = "GitHub:ClientId";
        public const string ClientSecret = "GitHub:ClientSecret";
        public const string CallbackPath = "GitHub:CallbackPath";

        /// <summary>Server-side Personal Access Token, used when the caller has no GitHub token.</summary>
        public const string Pat = "GitHub:PAT";
        public const string LocalReposPath = "GitHub:LocalReposPath";
    }

    /// <summary>Microsoft / Entra ID OAuth (the <c>/common</c> endpoint, Rule 3.3).</summary>
    public static class Microsoft
    {
        public const string ClientId = "Microsoft:ClientId";
        public const string ClientSecret = "Microsoft:ClientSecret";

        /// <summary>Comma-separated tenant allow-list; empty accepts every tenant.</summary>
        public const string AllowedTenants = "Microsoft:AllowedTenants";
    }

    /// <summary>Telemetry export targets.</summary>
    public static class Telemetry
    {
        public const string AppInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";
        public const string AppInsightsConnectionStringSection = "ApplicationInsights:ConnectionString";
        public const string AppInsightsInstrumentationKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
        public const string AppInsightsInstrumentationKeySection = "ApplicationInsights:InstrumentationKey";
        public const string OtlpEndpoint = "OpenTelemetry:OtlpEndpoint";
        public const string EnableConsoleExporters = "EnableConsoleExporters";
    }

    /// <summary>Runtime strategy selectors (GoF Strategy — see the mock-data banner, Rule 4.2).</summary>
    public static class FeatureFlags
    {
        public const string EnableMockDataForTesting = "FeatureFlags:EnableMockDataForTesting";
        public const string EnableGitHubApi = "FeatureFlags:EnableGitHubApi";
        public const string EnableBackgroundAnalysis = "FeatureFlags:EnableBackgroundAnalysis";
        public const string EnableOpenTelemetryExport = "FeatureFlags:EnableOpenTelemetryExport";
    }

    /// <summary>Client-facing chart tuning.</summary>
    public static class ChartSettings
    {
        public const string MaxLinesOfCode = "ChartSettings:MaxLinesOfCode";
    }
}
