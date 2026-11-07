using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Starter.Web.Infrastructure;

internal static class HostingSetup
{
    /// <summary>
    /// Security headers for the Blazor app. Blazor Server needs only same-origin scripts and a same-origin
    /// WebSocket ('self' covers wss: to the same host). form-action must allow the identity server because
    /// the sign-out form redirects there.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string identityOrigin)
    {
        var csp = string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            // Blazor's reconnect UI and some framework components set inline style attributes.
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'",
            $"form-action 'self' {identityOrigin}");

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = csp;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });
    }

    /// <summary>Implements <c>--health-check &lt;url&gt;</c> for the container health check. Certificate validation stays on.</summary>
    public static async Task<int> RunHealthCheckAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync(new Uri(url));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Health check failed: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>Readiness check for an HTTP dependency (identity server discovery document, API liveness).</summary>
internal sealed class HttpDependencyHealthCheck(IHttpClientFactory httpClientFactory, Uri url) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("health");
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Status code {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Not reachable");
        }
    }
}
