using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Starter.Api.Tests.Infrastructure;

/// <summary>
/// Hand-made access tokens for the negative cases that the real identity server never issues (expired, wrong
/// audience, missing scope, foreign signing key). A valid token made the same way is accepted - that positive
/// control proves the rejections below are caused by the one property each test changes.
/// </summary>
public static class TestTokens
{
    public static string Create(
        X509Certificate2 signingCertificate,
        string subject,
        string issuer = StarterFixture.IdentityUrl,
        string audience = "starter-api",
        string scope = "openid starter_api",
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", scope), new Claim("name", "Minted")]),
            IssuedAt = (notBefore ?? now.AddMinutes(-1)),
            NotBefore = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(5),
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(new X509SecurityKey(signingCertificate), SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>A certificate the identity server does not know: tokens signed with it must be rejected.</summary>
    public static X509Certificate2 ForeignCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Attacker", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
