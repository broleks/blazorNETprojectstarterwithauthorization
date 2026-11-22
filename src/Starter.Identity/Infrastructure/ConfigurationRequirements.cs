namespace Starter.Identity.Infrastructure;

/// <summary>Everything Starter.Identity needs to start, checked at once (see ConfigurationCheck).</summary>
internal static class ConfigurationRequirements
{
    public static void Check(ConfigurationCheck check)
    {
        check.Required("ConnectionStrings:Default", "SQL Server connection for the identity schema")
            .Url("Authentication:Issuer", required: !check.IsDevelopment, hint: "the public URL of this identity server")
            .Url("Authentication:DefaultReturnUrl", required: false)
            .Url("Api:BaseUrl", hint: "the API, used for account deletion and data export")
            .Duration("OpenIddict:AccessTokenLifetime")
            .Duration("OpenIddict:RefreshTokenLifetime")
            .Number("RateLimiting:AccountPostsPerMinute", 1, 100_000)
            .Number("RateLimiting:TokenRequestsPerMinute", 1, 1_000_000);

        // Token certificates: required outside Development (there, development certificates are used if missing).
        check.Certificate("OpenIddict:SigningCertificate:Path", "OpenIddict:SigningCertificate:Password", required: !check.IsDevelopment)
            .Certificate("OpenIddict:EncryptionCertificate:Path", "OpenIddict:EncryptionCertificate:Password", required: !check.IsDevelopment);

        CheckClients(check);

        // Email: needed for confirmation and password reset. Unencrypted SMTP only for local test inboxes.
        check.Required("Email:Host", "SMTP server; locally Mailpit")
            .Number("Email:Port", 1, 65535)
            .Email("Email:FromAddress")
            .OneOf("Email:Security", "None", "StartTls", "SslOnConnect")
            .Number("Email:MaxPerRecipientPerHour", 1, 1000)
            .When(check.IsProduction && string.Equals(check.Value("Email:Security"), "None", StringComparison.OrdinalIgnoreCase),
                c => c.Add("Email:Security is None in Production; use StartTls or SslOnConnect."));

        // Google: both values or none (none = Google sign-in switched off).
        var googleId = check.Value("Authentication:Google:ClientId");
        var googleSecret = check.Value("Authentication:Google:ClientSecret");
        check.When((googleId is null) != (googleSecret is null),
            c => c.Add("Authentication:Google:ClientId and ClientSecret must both be set, or both be empty to switch Google off."));

        foreach (var (key, value) in Children(check, "Admin:InitialAdmins"))
        {
            check.When(!string.IsNullOrWhiteSpace(value), c => c.Email(key));
        }

        foreach (var (key, value) in Children(check, "Cors:AllowedOrigins"))
        {
            check.When(!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.AbsolutePath != "/",
                c => c.Add($"{key} must be an origin such as https://localhost:7002 (no path)."));
        }
    }

    private static void CheckClients(ConfigurationCheck check)
    {
        var clients = check.Configuration.GetSection("OpenIddict:Clients").GetChildren().ToList();
        if (clients.All(c => c.Key != "starter-web"))
        {
            check.Add("OpenIddict:Clients:starter-web is missing (the web application's registration).");
        }

        check.Required("OpenIddict:Clients:starter-web:ClientSecret", "must equal Authentication:ClientSecret of Starter.Web")
            .Required("OpenIddict:Clients:starter-identity-service:ClientSecret", "service client for account deletion and export");

        foreach (var client in clients)
        {
            var prefix = $"OpenIddict:Clients:{client.Key}";
            var kind = client["Kind"] ?? nameof(ClientKind.Interactive);
            check.OneOf($"{prefix}:Kind", nameof(ClientKind.Interactive), nameof(ClientKind.Service));
            if (!kind.Equals(nameof(ClientKind.Interactive), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var redirectUris = client.GetSection("RedirectUris").GetChildren().ToList();
            check.When(redirectUris.Count == 0, c => c.Add($"{prefix}:RedirectUris needs at least one address."));
            foreach (var uri in redirectUris.Concat(client.GetSection("PostLogoutRedirectUris").GetChildren()))
            {
                // https for web addresses; custom schemes (com.example.app:/callback) are fine for mobile apps.
                var valid = Uri.TryCreate(uri.Value, UriKind.Absolute, out var parsed) && parsed.Scheme != Uri.UriSchemeHttp;
                check.When(!valid, c => c.Add($"{prefix}:{uri.Path.Split(':')[^2]}:{uri.Key} must be an absolute https (or app-specific) URI."));
            }
        }
    }

    private static IEnumerable<(string Key, string? Value)> Children(ConfigurationCheck check, string section) =>
        check.Configuration.GetSection(section).GetChildren().Select(c => (c.Path, c.Value));
}
