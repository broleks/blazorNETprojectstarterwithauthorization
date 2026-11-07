using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Starter.Web.Infrastructure.Sessions;

namespace Starter.Web.Infrastructure.Authentication;

/// <summary>
/// Talks to the identity server's token and revocation endpoints (server to server, authenticated with the
/// client secret). Endpoint addresses come from the discovery document the OpenID Connect handler already loads.
/// </summary>
public sealed class OidcTokenClient(IOptionsMonitor<OpenIdConnectOptions> oidcOptions, TimeProvider clock, ILogger<OidcTokenClient> logger)
{
    public enum Outcome { Success, Rejected, Unavailable }

    public sealed record RefreshResult(Outcome Outcome, SessionTokens? Tokens = null);

    public async Task<RefreshResult> RefreshAsync(SessionTokens current, CancellationToken cancellationToken)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            using var response = await options.Backchannel.PostAsync(
                new Uri(configuration.TokenEndpoint),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken!,
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!,
                }),
                cancellationToken);

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = json.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                // invalid_grant: the refresh token expired, was revoked (sign-out, password change) or was already used.
                // The error code is logged, the token itself never is.
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                logger.LogInformation("Token refresh rejected by the identity server: {Error}", error);
                return (int)response.StatusCode >= 500 ? new RefreshResult(Outcome.Unavailable) : new RefreshResult(Outcome.Rejected);
            }

            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;
            return new RefreshResult(Outcome.Success, new SessionTokens(
                root.GetProperty("access_token").GetString()!,
                // Refresh tokens are rotated: the new one replaces the old one, which is now spent.
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : current.RefreshToken,
                root.TryGetProperty("id_token", out var it) ? it.GetString() : current.IdToken,
                clock.GetUtcNow().AddSeconds(expiresIn)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(ex, "Token refresh failed because the identity server could not be reached");
            return new RefreshResult(Outcome.Unavailable);
        }
    }

    /// <summary>Revokes a refresh token (RFC 7009). Best effort: sign-out continues even if this fails.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            if (!configuration.AdditionalData.TryGetValue("revocation_endpoint", out var endpoint) || endpoint is null)
            {
                logger.LogWarning("The identity server does not advertise a revocation endpoint");
                return;
            }

            using var response = await options.Backchannel.PostAsync(
                new Uri(endpoint.ToString()!),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!,
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Refresh token revocation returned {StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Refresh token revocation failed");
        }
    }
}
