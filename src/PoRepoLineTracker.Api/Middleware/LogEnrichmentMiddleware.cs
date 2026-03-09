using Serilog.Context;
using System.Security.Claims;

namespace PoRepoLineTracker.Api.Middleware;

/// <summary>
/// Middleware that enriches logs with correlation IDs and session IDs.
/// Each HTTP request gets a unique correlation ID for distributed tracing,
/// and authenticated requests are tagged with their session ID (user-based).
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

        // Extract session ID from authenticated user (UserId claim)
        var sessionId = context.User?.FindFirst("UserId")?.Value ?? "anonymous";

        // Push correlation and session IDs to Serilog context
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            // Add correlation ID to response headers for client-side tracing
            context.Response.Headers["X-Correlation-ID"] = correlationId;

            await _next(context);
        }
    }
}
