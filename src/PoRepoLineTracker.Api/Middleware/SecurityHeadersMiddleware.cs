namespace PoRepoLineTracker.Api.Middleware;

/// <summary>
/// Middleware that adds security headers to HTTP responses to protect against common web vulnerabilities.
/// Implements headers for:
/// - XSS Protection
/// - Clickjacking Protection (Clickjack attacks)
/// - MIME Sniffing Protection
/// - HSTS (Strict Transport Security)
/// - Referrer Policy
/// - Content Security Policy
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // X-Content-Type-Options: Prevents MIME type sniffing attacks
        // Instructs browsers to respect the Content-Type header and not try to detect it
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // X-Frame-Options: Prevents clickjacking attacks
        // DENY = The page cannot be displayed in a frame (most restrictive)
        // SAMEORIGIN = The page can only be displayed in a frame if the frame origin matches the page origin
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // X-XSS-Protection: Legacy XSS protection (modern browsers use CSP instead)
        // 1; mode=block = Enable XSS filter and block page if attack detected
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

        // Referrer-Policy: Controls how much referrer information is shared
        // strict-origin-when-cross-origin: Send origin only on cross-origin requests
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Content-Security-Policy (CSP): Restricts sources of content that can be loaded
        // NOTE: Blazor WASM requires 'unsafe-eval' to compile WebAssembly at runtime
        // This is safe because the code is not executed from user input
        var cspHeader = "default-src 'self'; " +
                        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https:; " +  // unsafe-eval allows Blazor WASM compilation
                        "style-src 'self' 'unsafe-inline' https:; " +    // Allow inline styles
                        "img-src 'self' data: https:; " +
                        "font-src 'self' https:; " +
                        "connect-src 'self' https:; " +
                        "frame-ancestors 'none'; " +
                        "upgrade-insecure-requests;";

        context.Response.Headers["Content-Security-Policy"] = cspHeader;

        // Permissions-Policy (formerly Feature-Policy): Controls browser features
        // Restrict certain APIs to prevent misuse
        context.Response.Headers["Permissions-Policy"] = "geolocation=(), " +
                                                         "microphone=(), " +
                                                         "camera=(), " +
                                                         "payment=()";

        // Strict-Transport-Security (HSTS): Forces HTTPS connections
        // This header is only sent over HTTPS connections
        if (context.Request.IsHttps)
        {
            // max-age: 31536000 = 1 year in seconds
            // includeSubDomains: Apply policy to all subdomains
            // preload: Allow domain to be included in browser HSTS preload lists
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        // Additional security headers for additional protection
        // Prevents the browser from prefetching DNS queries
        context.Response.Headers["X-DNS-Prefetch-Control"] = "off";

        _logger.LogDebug("Security headers applied to response for path {Path}", context.Request.Path);

        await _next(context);
    }
}
