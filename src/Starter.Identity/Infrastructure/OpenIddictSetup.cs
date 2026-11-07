using System.Security.Cryptography.X509Certificates;
using OpenIddict.Abstractions;
using Starter.Contracts;
using Starter.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Starter.Identity.Infrastructure;

/// <summary>OpenIddict authorization server configuration: which flows, endpoints, scopes and keys are used.</summary>
internal static class OpenIddictSetup
{
    public static void AddAuthorizationServer(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(OpenIddictSettings.SectionName).Get<OpenIddictSettings>() ?? new OpenIddictSettings();
        var issuer = builder.Configuration["Authentication:Issuer"];

        builder.Services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
            .AddServer(options =>
            {
                // A fixed issuer keeps tokens valid regardless of how the request reached the app
                // (reverse proxy, internal host name). Required outside Development.
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    options.SetIssuer(new Uri(issuer, UriKind.Absolute));
                }
                else if (!builder.Environment.IsDevelopment())
                {
                    throw new InvalidOperationException("'Authentication:Issuer' must be configured outside Development.");
                }

                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetRevocationEndpointUris("connect/revoke");

                // Authorization Code + PKCE for every client (web today, mobile later) and refresh tokens.
                // No implicit, password or client-credentials flows: they are not needed by this starter.
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();

                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, ApiConstants.Scope);
                options.RegisterAudiences(ApiConstants.Audience);

                options.SetAccessTokenLifetime(settings.AccessTokenLifetime)
                    .SetRefreshTokenLifetime(settings.RefreshTokenLifetime);

                // Access tokens are signed JWTs (not encrypted) so the API can validate them with only the
                // public keys from the discovery document, and no secret has to be shared with the API.
                // They never reach the browser: Starter.Web keeps them on the server.
                options.DisableAccessTokenEncryption();

                AddCertificates(options, settings, builder.Environment);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            });
    }

    private static void AddCertificates(OpenIddictServerBuilder options, OpenIddictSettings settings, IWebHostEnvironment environment)
    {
        var signing = LoadCertificate(settings.SigningCertificate);
        var encryption = LoadCertificate(settings.EncryptionCertificate);

        if (signing is not null && encryption is not null)
        {
            options.AddSigningCertificate(signing).AddEncryptionCertificate(encryption);
        }
        else if (environment.IsDevelopment())
        {
            // Self-signed certificates stored in the current user's certificate store. Fine for a developer
            // machine; never used in production because they are not shared between instances.
            options.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
        }
        else
        {
            throw new InvalidOperationException(
                "OpenIddict signing and encryption certificates are not configured. Set OpenIddict:SigningCertificate and " +
                "OpenIddict:EncryptionCertificate (see docs/azure-deployment.md).");
        }
    }

    private static X509Certificate2? LoadCertificate(CertificateSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Path))
        {
            return null;
        }

        // EphemeralKeySet keeps the private key in memory only; it works for non-root containers
        // without a writable key store.
        return X509CertificateLoader.LoadPkcs12FromFile(settings.Path, settings.Password, X509KeyStorageFlags.EphemeralKeySet);
    }
}
