namespace Starter.Identity.Infrastructure;

/// <summary>Configuration section "OpenIddict". Secrets (client secrets, certificate passwords) come from user secrets, environment variables or Key Vault.</summary>
public sealed class OpenIddictSettings
{
    public const string SectionName = "OpenIddict";

    /// <summary>Short lifetime limits how long a token stays usable after sign-out (see docs/security.md).</summary>
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Refresh tokens are rotated on every use; each new one gets this lifetime (sliding expiration).</summary>
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromHours(8);

    public CertificateSettings SigningCertificate { get; init; } = new();
    public CertificateSettings EncryptionCertificate { get; init; } = new();

    /// <summary>Client applications keyed by client id. They are created or updated by the migration step.</summary>
    public Dictionary<string, ClientSettings> Clients { get; init; } = [];
}

public sealed class CertificateSettings
{
    /// <summary>Path to a PFX/PKCS#12 file. On Azure App Service this is /var/ssl/private/&lt;thumbprint&gt;.p12.</summary>
    public string? Path { get; init; }
    public string? Password { get; init; }
}

public sealed class ClientSettings
{
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Set for confidential (server-side) clients such as Starter.Web. Leave empty for public clients (mobile apps), which rely on PKCE only.</summary>
    public string? ClientSecret { get; init; }

    public List<string> RedirectUris { get; init; } = [];
    public List<string> PostLogoutRedirectUris { get; init; } = [];
}
