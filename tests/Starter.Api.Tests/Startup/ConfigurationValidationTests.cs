using Starter.Api.Tests.Infrastructure;
using Starter.Observability;

namespace Starter.Api.Tests.Startup;

/// <summary>The applications refuse to start with incomplete configuration and say exactly what is wrong.</summary>
public sealed class ConfigurationValidationTests(StarterFixture fixture)
{
    [Fact]
    public void Api_lists_every_missing_setting_at_once()
    {
        using var api = new ApiFactory(fixture, new()
        {
            ["ConnectionStrings:Default"] = "",
            ["Authentication:Issuer"] = "",
            ["RateLimiting:RequestsPerMinute"] = "0",
        });

        var error = StartupError(() => api.Server);

        Assert.Equal(3, error.Problems.Count);
        Assert.Contains(error.Problems, p => p.StartsWith("ConnectionStrings:Default is missing", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("Authentication:Issuer is missing", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("RateLimiting:RequestsPerMinute must be", StringComparison.Ordinal));
        Assert.Contains("Starter.Api cannot start - 3 configuration problems", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_reports_problems_without_revealing_values()
    {
        const string secretMarker = "do-not-print-this-value";
        using var identity = new IdentityFactory(fixture, new()
        {
            ["Email:Host"] = "",
            ["Authentication:Google:ClientId"] = secretMarker,
            ["OpenIddict:SigningCertificate:Path"] = "/nonexistent/signing.pfx",
            ["OpenIddict:Clients:starter-web:RedirectUris:0"] = "http://insecure.example/callback",
            ["Cors:AllowedOrigins:0"] = "https://localhost:7002/with/path",
            ["ActivityLogging:Destination"] = "Elasticsearch",
        });

        var error = StartupError(() => identity.Server);

        Assert.Contains(error.Problems, p => p.StartsWith("Email:Host is missing", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("Authentication:Google:ClientId and ClientSecret", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("OpenIddict:SigningCertificate:Path points to a file that does not exist", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("OpenIddict:Clients:starter-web:RedirectUris:0 must be", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("Cors:AllowedOrigins:0 must be an origin", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("ActivityLogging:Elasticsearch:Url is missing", StringComparison.Ordinal));
        Assert.DoesNotContain(secretMarker, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_configuration_starts_normally()
    {
        // The shared fixture's applications are configured completely and are already running.
        Assert.NotNull(fixture.Api.Server);
        Assert.NotNull(fixture.Identity.Server);
    }

    private static ConfigurationValidationException StartupError(Func<object> start)
    {
        var exception = Assert.ThrowsAny<Exception>(start);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ConfigurationValidationException validation)
            {
                return validation;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected a configuration error but got: {exception}");
    }
}
