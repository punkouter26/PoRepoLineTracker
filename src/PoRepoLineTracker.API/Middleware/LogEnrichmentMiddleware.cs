using Serilog.Context;
using System.Security.Claims;

namespace PoRepoLineTracker.API.Middleware;

/// <summary>
/// Middleware that enriches logs with correlation IDs and session IDs.
/// Each HTTP request gets a unique correlation ID for distributed tracing,
/// and authenticated requests are tagged with their UserId and SessionId.
/// </summary>
public class LogEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public LogEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate or retrieve correlation ID for this request
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationIdHeader)
            ? correlationIdHeader.ToString()
            : Guid.NewGuid().ToString("N");

        // UserId from the custom claim — distinct from session
        var userId = context.User?.FindFirst(ClaimsPrincipalExtensions.UserIdClaim)?.Value ?? "anonymous";

        // SessionId from the standard NameIdentifier claim (OAuth subject / GUID)
        var sessionId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        // Push all three properties to Serilog context for every log line in this request
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            // Add correlation ID to response headers for client-side tracing
            context.Response.Headers["X-Correlation-ID"] = correlationId;

            await _next(context);
        }
    }
}
