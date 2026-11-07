using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Starter.Api.Infrastructure;

/// <summary>
/// Basic per-user request limits. They protect the database from a misbehaving client; they are not a
/// replacement for platform-level DDoS protection. Limits are read from configuration (section "RateLimiting").
/// Counters are kept in memory, so with several instances each instance enforces its own limit.
/// </summary>
internal static class RateLimitingSetup
{
    public const string WritePolicy = "writes";

    public static void AddApiRateLimiting(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new RateLimitSettings();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global limit for every request, partitioned by user (or by IP before authentication succeeds).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));

            // A stricter limit for operations that write to the database.
            options.AddPolicy(WritePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.WritesPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await TypedResults.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        detail: "Too many requests. Try again in a moment.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? "user:" + context.User.GetUserId()
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private sealed class RateLimitSettings
    {
        public int RequestsPerMinute { get; init; } = 300;
        public int WritesPerMinute { get; init; } = 60;
    }
}
