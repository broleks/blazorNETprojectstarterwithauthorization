using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using Starter.Contracts;
using Starter.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Starter.Identity.Infrastructure;

/// <summary>Creates token principals and ends sessions when account credentials change.</summary>
public sealed class AccountSecurity(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    ILogger<AccountSecurity> logger)
{
    /// <summary>
    /// Private claim carried in authorization codes and refresh tokens (never in access or identity tokens).
    /// The token endpoint compares it with the current security stamp, so a password change or reset
    /// invalidates refresh tokens even if revocation below were to miss one.
    /// </summary>
    public const string SecurityStampClaim = "starter_security_stamp";

    /// <summary>
    /// Revokes every authorization and token issued to the user (all devices and browsers). Refresh tokens stop
    /// working immediately; already issued access tokens stay valid until they expire (5 minutes by default).
    /// </summary>
    public async Task RevokeAllSessionsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var revokedAuthorizations = await authorizations.RevokeBySubjectAsync(user.Id, cancellationToken);
        var revokedTokens = await tokens.RevokeBySubjectAsync(user.Id, cancellationToken);
        logger.LogInformation("Revoked {Authorizations} authorizations and {Tokens} tokens for user {UserId}",
            revokedAuthorizations, revokedTokens, user.Id);
    }

    /// <summary>Builds the identity that OpenIddict turns into tokens. Claims are fresh from the database.</summary>
    public static ClaimsPrincipal CreateTokenPrincipal(ApplicationUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "Starter.Identity",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id)
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(SecurityStampClaim, user.SecurityStamp);

        identity.SetScopes(scopes);
        // The API audience is added only when the client asked for the API scope.
        identity.SetResources(scopes.Contains(ApiConstants.Scope) ? [ApiConstants.Audience] : []);
        identity.SetDestinations(claim => GetDestinations(claim, identity));

        return new ClaimsPrincipal(identity);
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsIdentity identity)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;

            case Claims.Name when identity.HasScope(Scopes.Profile):
            case Claims.Email when identity.HasScope(Scopes.Email):
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;

            // Everything else (security stamp, internal claims) stays out of access and identity tokens.
            default:
                yield break;
        }
    }
}
