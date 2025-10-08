using System.Net.Http.Json;
using System.Text.Json;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Client-side logger that sends log entries to the server for centralized logging.
/// In development, logs are sent to /api/log/client endpoint.
/// </summary>
public class ClientLogger
{
    private readonly HttpClient _httpClient;
    private readonly bool _isDevelopment;

    public ClientLogger(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Determine if we're in development mode (simplified check)
        _isDevelopment = true; // In production, this would be configured differently
    }

    public async Task LogInformationAsync(string message, Dictionary<string, object>? properties = null)
    {
        await LogAsync("Information", message, null, properties);
    }

    public async Task LogWarningAsync(string message, Dictionary<string, object>? properties = null)
    {
        await LogAsync("Warning", message, null, properties);
    }

    public async Task LogErrorAsync(string message, Exception? exception = null, Dictionary<string, object>? properties = null)
    {
        await LogAsync("Error", message, exception?.ToString(), properties);
    }

    public async Task LogDebugAsync(string message, Dictionary<string, object>? properties = null)
    {
        await LogAsync("Debug", message, null, properties);
    }

    private async Task LogAsync(string level, string message, string? exception = null, Dictionary<string, object>? properties = null)
    {
        // Only send logs to server in development mode
        if (!_isDevelopment)
        {
            // In production, you might want to use a different strategy
            Console.WriteLine($"[{level}] {message}");
            return;
        }

        try
        {
            var logEntry = new
            {
                Level = level,
                Message = message,
                Exception = exception,
                Properties = properties ?? new Dictionary<string, object>()
            };

            await _httpClient.PostAsJsonAsync("/api/log/client", logEntry);
        }
        catch
        {
            // Fallback to console logging if server logging fails
            Console.WriteLine($"[{level}] {message}");
            if (exception != null)
            {
                Console.WriteLine($"Exception: {exception}");
            }
        }
    }

    /// <summary>
    /// Tracks a custom event with properties for analytics.
    /// </summary>
    public async Task TrackEventAsync(string eventName, Dictionary<string, object>? properties = null)
    {
        var props = properties ?? new Dictionary<string, object>();
        props["EventName"] = eventName;
        await LogInformationAsync($"Event: {eventName}", props);
    }

    /// <summary>
    /// Tracks a page view for analytics.
    /// </summary>
    public async Task TrackPageViewAsync(string pageName, Dictionary<string, object>? properties = null)
    {
        var props = properties ?? new Dictionary<string, object>();
        props["PageName"] = pageName;
        await LogInformationAsync($"PageView: {pageName}", props);
    }

    /// <summary>
    /// Tracks a metric value for performance monitoring.
    /// </summary>
    public async Task TrackMetricAsync(string metricName, double value, Dictionary<string, object>? properties = null)
    {
        var props = properties ?? new Dictionary<string, object>();
        props["MetricName"] = metricName;
        props["Value"] = value;
        await LogInformationAsync($"Metric: {metricName} = {value}", props);
    }
}
