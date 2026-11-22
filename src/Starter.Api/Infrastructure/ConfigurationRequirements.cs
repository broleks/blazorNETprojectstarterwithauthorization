using Starter.Observability;

namespace Starter.Api.Infrastructure;

/// <summary>Everything Starter.Api needs to start, checked at once (see ConfigurationCheck).</summary>
internal static class ConfigurationRequirements
{
    public static void Check(ConfigurationCheck check) => check
        .Required("ConnectionStrings:Default", "SQL Server connection for the app schema")
        .Url("Authentication:Issuer", hint: "the public URL of Starter.Identity, exactly as in its tokens")
        .Number("RateLimiting:RequestsPerMinute", 1, 1_000_000)
        .Number("RateLimiting:WritesPerMinute", 1, 1_000_000)
        .Url("OpenApi:PublicUrl", required: false);
}
