using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Starter.Contracts;

namespace Starter.Api.Infrastructure;

/// <summary>
/// The single place where API authentication and authorization are configured.
/// Endpoints never deal with tokens: they get an authenticated <see cref="ClaimsPrincipal"/> or a 401/403.
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>Policy applied to every API endpoint: a valid access token for this API with the API scope.</summary>
    public const string ApiPolicy = "starter-api";

    public static void AddApiAuthentication(this WebApplicationBuilder builder)
    {
        var issuer = builder.Configuration["Authentication:Issuer"]
            ?? throw new InvalidOperationException("'Authentication:Issuer' is not configured. It must be the public URL of Starter.Identity.");

        builder.Services.AddOpenIddict()
            .AddValidation(options =>
            {
                // Tokens are validated locally: the signing keys are downloaded from the issuer's
                // discovery document (JWKS) and cached, so a request does not call the identity server.
                options.SetIssuer(issuer);

                // Only tokens issued for this API (aud = starter-api) are accepted. A token issued
                // for another resource - or a Google token - is rejected even if correctly signed.
                options.AddAudiences(ApiConstants.Audience);

                options.UseSystemNetHttp();
                options.UseAspNetCore();
            });

        builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        var apiPolicy = new AuthorizationPolicyBuilder(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(OpenIddictConstants.Claims.Subject)
            // A valid token without the API scope (e.g. one issued only for sign-in) gets 403.
            .RequireAssertion(context => context.User.HasScope(ApiConstants.Scope))
            .Build();

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ApiPolicy, apiPolicy)
            // Secure by default: any endpoint that is not explicitly anonymous (health checks) requires the API policy,
            // so a new endpoint cannot be exposed by forgetting an attribute.
            .SetFallbackPolicy(apiPolicy);
    }

    /// <summary>The user id the API trusts: the <c>sub</c> claim of the validated access token.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(OpenIddictConstants.Claims.Subject)
        ?? throw new InvalidOperationException("The authenticated principal has no subject claim.");
}
