using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Starter.Web.Infrastructure.Sessions;

/// <summary>OAuth tokens of one session. Only ever held on the server; never sent to the browser.</summary>
public sealed record SessionTokens(string AccessToken, string? RefreshToken, string? IdToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Reads the tokens the OpenID Connect handler put into the authentication properties (SaveTokens = true).</summary>
    public static SessionTokens? FromProperties(AuthenticationProperties properties)
    {
        var accessToken = properties.GetTokenValue("access_token");
        if (accessToken is null)
        {
            return null;
        }

        var expiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return new SessionTokens(accessToken, properties.GetTokenValue("refresh_token"), properties.GetTokenValue("id_token"), expiresAt);
    }
}

/// <summary>Encrypts what is stored in <see cref="WebSession"/> with Data Protection (keys are persisted in the database).</summary>
public sealed class SessionProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector ticketProtector = provider.CreateProtector("Starter.Web.Sessions.Ticket.v1");
    private readonly IDataProtector tokenProtector = provider.CreateProtector("Starter.Web.Sessions.Tokens.v1");

    public byte[] Protect(AuthenticationTicket ticket) => ticketProtector.Protect(TicketSerializer.Default.Serialize(ticket));

    public byte[] Protect(SessionTokens tokens) => tokenProtector.Protect(JsonSerializer.SerializeToUtf8Bytes(tokens));

    public AuthenticationTicket? UnprotectTicket(byte[] data) =>
        TryUnprotect(ticketProtector, data) is { } bytes ? TicketSerializer.Default.Deserialize(bytes) : null;

    public SessionTokens? UnprotectTokens(byte[] data) =>
        TryUnprotect(tokenProtector, data) is { } bytes ? JsonSerializer.Deserialize<SessionTokens>(bytes) : null;

    // A session written with a key that no longer exists (e.g. key ring reset) is treated as signed out.
    private static byte[]? TryUnprotect(IDataProtector protector, byte[] data)
    {
        try
        {
            return protector.Unprotect(data);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
