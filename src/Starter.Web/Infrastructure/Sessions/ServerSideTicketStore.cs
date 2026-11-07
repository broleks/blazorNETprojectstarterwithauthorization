using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Starter.Web.Infrastructure.Sessions;

/// <summary>
/// Keeps authentication tickets on the server (cookie handler "session store"). The browser cookie holds a random
/// session key only, so tokens never leave the server, a session can be ended server-side (sign-out, failed
/// token refresh), and every instance of the app sees the same sessions.
/// </summary>
public sealed class ServerSideTicketStore(
    IDbContextFactory<WebDbContext> dbFactory,
    SessionProtector protector,
    TimeProvider clock) : ITicketStore
{
    /// <summary>Claim added to the principal so components (which have no HttpContext) can find their session.</summary>
    public const string SessionKeyClaim = "starter_session";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokens = TakeTokens(ticket)
            ?? throw new InvalidOperationException("A new session must contain the tokens from the sign-in.");
        SetSessionKey(ticket, key);

        var now = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Sessions.Add(new WebSession
        {
            Key = key,
            UserId = UserIdOf(ticket),
            Ticket = protector.Protect(ticket),
            Tokens = protector.Protect(tokens),
            CreatedAt = now,
            ExpiresAt = ticket.Properties.ExpiresUtc ?? now.Add(DefaultLifetime),
        });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (session is null)
        {
            // The session was ended (sign-out elsewhere, failed refresh). Never bring it back.
            return;
        }

        // Tokens are present only when this renewal is a fresh sign-in in a browser that already had a session.
        // A plain sliding-expiration renewal has no tokens and must not touch the stored (possibly newer) ones.
        if (TakeTokens(ticket) is { } tokens)
        {
            session.Tokens = protector.Protect(tokens);
            session.UserId = UserIdOf(ticket);
        }

        SetSessionKey(ticket, key);
        session.Ticket = protector.Protect(ticket);
        session.ExpiresAt = ticket.Properties.ExpiresUtc ?? clock.GetUtcNow().Add(DefaultLifetime);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (session is null || session.ExpiresAt <= clock.GetUtcNow())
        {
            return null;
        }

        return protector.UnprotectTicket(session.Ticket);
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    private static SessionTokens? TakeTokens(AuthenticationTicket ticket)
    {
        var tokens = SessionTokens.FromProperties(ticket.Properties);
        // Remove the tokens from the ticket; they are stored (encrypted) in their own column.
        ticket.Properties.StoreTokens([]);
        return tokens;
    }

    private static void SetSessionKey(AuthenticationTicket ticket, string key)
    {
        var identity = ticket.Principal.Identities.First();
        foreach (var existing in identity.FindAll(SessionKeyClaim).ToList())
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(SessionKeyClaim, key));
    }

    private static string UserIdOf(AuthenticationTicket ticket) =>
        ticket.Principal.FindFirstValue("sub") ?? throw new InvalidOperationException("The signed-in principal has no 'sub' claim.");
}

/// <summary>Connects the store to the cookie handler (the store needs DI services, so it is set after registration).</summary>
internal sealed class ConfigureCookieSessionStore(ServerSideTicketStore store)
    : Microsoft.Extensions.Options.IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme)
        {
            options.SessionStore = store;
        }
    }
}

/// <summary>Deletes expired sessions once an hour so the table does not grow forever.</summary>
internal sealed class ExpiredSessionCleanup(IDbContextFactory<WebDbContext> dbFactory, TimeProvider clock, ILogger<ExpiredSessionCleanup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var now = clock.GetUtcNow();
                var deleted = await db.Sessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} expired sessions", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Expired session cleanup failed; it will be retried");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
