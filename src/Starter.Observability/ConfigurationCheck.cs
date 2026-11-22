using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Starter.Observability;

/// <summary>
/// Startup check of the configuration. Each application declares what it needs; all problems are collected and
/// reported together, before anything else starts, instead of failing one setting at a time somewhere later.
/// Messages name the setting and how to fix it, never its value (it may be a secret).
/// </summary>
public sealed class ConfigurationCheck(IConfiguration configuration, IHostEnvironment environment)
{
    private readonly List<string> problems = [];

    public IConfiguration Configuration { get; } = configuration;
    public bool IsDevelopment => environment.IsDevelopment();
    public bool IsProduction => environment.IsProduction();
    public IReadOnlyList<string> Problems => problems;

    public string? Value(string key) => Configuration[key] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    public ConfigurationCheck Add(string problem)
    {
        problems.Add(problem);
        return this;
    }

    public ConfigurationCheck Required(string key, string? hint = null)
    {
        if (Value(key) is null)
        {
            Add($"{key} is missing{(hint is null ? "" : $" ({hint})")}.");
        }

        return this;
    }

    /// <summary>An absolute http(s) URL. HTTPS is required unless <paramref name="allowHttp"/> (e.g. a local Elasticsearch).</summary>
    public ConfigurationCheck Url(string key, bool required = true, bool allowHttp = false, string? hint = null)
    {
        var value = Value(key);
        if (value is null)
        {
            return required ? Required(key, hint) : this;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
        {
            Add($"{key} must be an absolute {(allowHttp ? "http(s)" : "https")} URL.");
        }

        return this;
    }

    public ConfigurationCheck Number(string key, int min, int max)
    {
        if (Value(key) is { } value && (!int.TryParse(value, out var number) || number < min || number > max))
        {
            Add($"{key} must be a whole number between {min} and {max}.");
        }

        return this;
    }

    public ConfigurationCheck Duration(string key)
    {
        if (Value(key) is { } value && (!TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var duration) || duration <= TimeSpan.Zero))
        {
            Add($"{key} must be a positive duration such as 00:05:00.");
        }

        return this;
    }

    public ConfigurationCheck OneOf(string key, params string[] allowed)
    {
        if (Value(key) is { } value && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            Add($"{key} must be one of: {string.Join(", ", allowed)}.");
        }

        return this;
    }

    public ConfigurationCheck Email(string key, bool required = true)
    {
        var value = Value(key);
        if (value is null)
        {
            return required ? Required(key) : this;
        }

        if (!MailAddress.TryCreate(value, out _))
        {
            Add($"{key} must be an email address.");
        }

        return this;
    }

    /// <summary>A PKCS#12 file that exists and opens with the configured password.</summary>
    public ConfigurationCheck Certificate(string pathKey, string passwordKey, bool required)
    {
        var path = Value(pathKey);
        if (path is null)
        {
            return required ? Required(pathKey, "path to a .pfx/.p12 file; see docs/azure-deployment.md") : this;
        }

        if (!File.Exists(path))
        {
            return Add($"{pathKey} points to a file that does not exist.");
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, Configuration[passwordKey], X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
            {
                Add($"{pathKey} has no private key.");
            }
        }
        catch (CryptographicException)
        {
            Add($"{pathKey} cannot be opened (wrong {passwordKey} or not a PKCS#12 file).");
        }

        return this;
    }

    public ConfigurationCheck When(bool condition, Action<ConfigurationCheck> checks)
    {
        if (condition)
        {
            checks(this);
        }

        return this;
    }

    /// <summary>The ActivityLogging section (shared by all applications).</summary>
    public ConfigurationCheck Logging()
    {
        const string section = ActivityLoggingOptions.SectionName;
        OneOf($"{section}:Destination", nameof(LogDestination.File), nameof(LogDestination.Elasticsearch), nameof(LogDestination.Both));
        Number($"{section}:RetentionDays", 1, 3650);
        var destination = Value($"{section}:Destination") ?? nameof(LogDestination.File);
        return When(!destination.Equals(nameof(LogDestination.File), StringComparison.OrdinalIgnoreCase),
            c => c.Url($"{section}:Elasticsearch:Url", allowHttp: true, hint: "needed because Destination includes Elasticsearch"));
    }
}

/// <summary>Thrown at startup when the configuration is incomplete; the message lists every problem.</summary>
public sealed class ConfigurationValidationException(string applicationName, IReadOnlyList<string> problems)
    : Exception(Format(applicationName, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    private static string Format(string applicationName, IReadOnlyList<string> problems) =>
        $"{applicationName} cannot start - {problems.Count} configuration problem{(problems.Count == 1 ? "" : "s")}:{Environment.NewLine}" +
        string.Join(Environment.NewLine, problems.Select(p => " - " + p)) + Environment.NewLine +
        "Locally run 'dotnet run scripts/setup-local.cs'. All settings: docs/configuration.md.";
}

public static class ConfigurationCheckExtensions
{
    /// <summary>
    /// Call first in Program.cs. Checks the shared logging section plus what <paramref name="checks"/> declares,
    /// and stops the application with one readable message if anything is wrong.
    /// </summary>
    public static WebApplicationBuilder ValidateConfiguration(this WebApplicationBuilder builder, string applicationName, Action<ConfigurationCheck> checks)
    {
        var check = new ConfigurationCheck(builder.Configuration, builder.Environment);
        check.Logging();
        checks(check);
        if (check.Problems.Count > 0)
        {
            throw new ConfigurationValidationException(applicationName, check.Problems);
        }

        return builder;
    }
}
