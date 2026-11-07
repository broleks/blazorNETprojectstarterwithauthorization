using Microsoft.EntityFrameworkCore;
using Starter.Web.Infrastructure.Sessions;

namespace Starter.Web.Infrastructure.Authentication;

public enum AccessTokenStatus { Ok, SessionEnded, IdentityUnavailable }

public readonly record struct AccessTokenResult(AccessTokenStatus Status, string? AccessToken = null);

/// <summary>
/// Gives out the access token of a session and refreshes it when needed.
///
/// It works from a session key only - not from HttpContext - because Blazor components in an interactive
/// circuit run long after the HTTP request that started the circuit, and HttpContext is not available there.
/// The service itself holds no tokens: every call reads the session from the database, so users can never
/// see each other's tokens and every instance of the app works with the same data.
/// </summary>
public sealed class SessionTokenService(
    IDbContextFactory<WebDbContext> dbFactory,
    SessionProtector protector,
    OidcTokenClient tokenClient,
    TimeProvider clock,
    ILogger<SessionTokenService> logger)
{
    /// <summary>Refresh a little before expiry so a token does not expire on its way to the API.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    // Serializes refreshes per session inside this instance (striped locks, so memory use is fixed).
    // Across instances, the identity server's refresh token reuse leeway covers concurrent refreshes.
    private static readonly SemaphoreSlim[] RefreshLocks = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];

    public async Task<AccessTokenResult> GetAccessTokenAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var tokens = await LoadAsync(sessionKey, cancellationToken);
        if (tokens is null)
        {
            return new(AccessTokenStatus.SessionEnded);
        }

        if (!NeedsRefresh(tokens))
        {
            return new(AccessTokenStatus.Ok, tokens.AccessToken);
        }

        var gate = RefreshLocks[(uint)StringComparer.Ordinal.GetHashCode(sessionKey) % RefreshLocks.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another request or circuit of the same session may have refreshed while we waited.
            tokens = await LoadAsync(sessionKey, cancellationToken);
            if (tokens is null)
            {
                return new(AccessTokenStatus.SessionEnded);
            }

            if (!NeedsRefresh(tokens))
            {
                return new(AccessTokenStatus.Ok, tokens.AccessToken);
            }

            if (tokens.RefreshToken is null)
            {
                await DeleteAsync(sessionKey, cancellationToken);
                return new(AccessTokenStatus.SessionEnded);
            }

            var result = await tokenClient.RefreshAsync(tokens, cancellationToken);
            switch (result.Outcome)
            {
                case OidcTokenClient.Outcome.Success:
                    await SaveTokensAsync(sessionKey, result.Tokens!, cancellationToken);
                    return new(AccessTokenStatus.Ok, result.Tokens!.AccessToken);

                case OidcTokenClient.Outcome.Rejected:
                    logger.LogInformation("Session ended because its refresh token was rejected");
                    await DeleteAsync(sessionKey, cancellationToken);
                    return new(AccessTokenStatus.SessionEnded);

                default:
                    // The identity server is down. The current token may still be usable for a few seconds.
                    return tokens.ExpiresAt > clock.GetUtcNow()
                        ? new(AccessTokenStatus.Ok, tokens.AccessToken)
                        : new(AccessTokenStatus.IdentityUnavailable);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> GetIdTokenAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        (await LoadAsync(sessionKey, cancellationToken))?.IdToken;

    /// <summary>Ends a session on the server: deletes it and revokes its refresh token at the identity server.</summary>
    public async Task EndSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var tokens = await LoadAsync(sessionKey, cancellationToken);
        await DeleteAsync(sessionKey, cancellationToken);
        if (tokens?.RefreshToken is { } refreshToken)
        {
            await tokenClient.RevokeAsync(refreshToken, cancellationToken);
        }
    }

    private bool NeedsRefresh(SessionTokens tokens) => tokens.ExpiresAt - clock.GetUtcNow() < RefreshMargin;

    private async Task<SessionTokens?> LoadAsync(string sessionKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.AsNoTracking()
            .Where(s => s.Key == sessionKey)
            .Select(s => new { s.Tokens, s.ExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);

        return session is null || session.ExpiresAt <= clock.GetUtcNow() ? null : protector.UnprotectTokens(session.Tokens);
    }

    private async Task SaveTokensAsync(string sessionKey, SessionTokens tokens, CancellationToken cancellationToken)
    {
        var protectedTokens = protector.Protect(tokens);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sessions.Where(s => s.Key == sessionKey)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Tokens, protectedTokens), cancellationToken);
    }

    private async Task DeleteAsync(string sessionKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sessions.Where(s => s.Key == sessionKey).ExecuteDeleteAsync(cancellationToken);
    }
}
