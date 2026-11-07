using System.Globalization;
using System.Threading.RateLimiting;

namespace Starter.Identity.Infrastructure;

/// <summary>Rate limits and HTTP security headers for the identity server.</summary>
internal static class WebSecurity
{
    /// <summary>
    /// Limits form posts to account pages (sign-in, registration, password reset, resend email) and token
    /// requests per client IP. Together with account lockout and the per-recipient email limit this slows
    /// down password guessing and email flooding. Counters are in memory (per instance).
    /// Behind a reverse proxy the client IP comes from X-Forwarded-For (see docs/azure-deployment.md).
    /// </summary>
    public static void AddAccountRateLimiting(this WebApplicationBuilder builder)
    {
        var accountPostsPerMinute = builder.Configuration.GetValue("RateLimiting:AccountPostsPerMinute", 20);
        var tokenRequestsPerMinute = builder.Configuration.GetValue("RateLimiting:TokenRequestsPerMinute", 600);

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var path = context.Request.Path;

                if (HttpMethods.IsPost(context.Request.Method) && path.StartsWithSegments("/Account"))
                {
                    return RateLimitPartition.GetFixedWindowLimiter("account:" + ip, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = accountPostsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    });
                }

                if (path.StartsWithSegments("/connect/token"))
                {
                    return RateLimitPartition.GetFixedWindowLimiter("token:" + ip, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = tokenRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    });
                }

                return RateLimitPartition.GetNoLimiter("none");
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                if (context.HttpContext.Request.Path.StartsWithSegments("/connect/token"))
                {
                    // Machine-to-machine endpoint: an OAuth-style JSON error.
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { error = "temporarily_unavailable", error_description = "Too many requests." }, cancellationToken);
                }

                // Pages: the empty 429 response is turned into the friendly error page by UseStatusCodePagesWithReExecute.
            };
        });
    }

    /// <summary>
    /// Adds security headers to every response. The CSP form-action list must contain the client origins:
    /// browsers apply it to the redirects that follow a form post (sign-in → authorize → client callback).
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, PublicUrls urls, bool googleEnabled)
    {
        var formTargets = string.Join(' ', urls.ClientOrigins.Append(googleEnabled ? "https://accounts.google.com" : string.Empty)).Trim();
        var csp = string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self'",
            "img-src 'self' data:",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'",
            $"form-action 'self' {formTargets}".Trim());

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
}
