using System.Text.Encodings.Web;
using MailKit.Net.Smtp;
using MailKit.Security;
using AuthenticationException = MailKit.Security.AuthenticationException;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Starter.Identity.Infrastructure.Email;

/// <summary>Sends the account emails (confirmation, password reset). Links are only ever delivered by email, never shown on a page.</summary>
public interface IAccountEmailSender
{
    Task SendConfirmationLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default);
    Task SendPasswordResetLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default);

    /// <summary>Sent when someone tries to register an email that already has an account, instead of revealing that on the page.</summary>
    Task SendAccountExistsNoticeAsync(string email, string displayName, string signInLink, CancellationToken cancellationToken = default);
}

internal sealed class SmtpAccountEmailSender(
    IOptions<EmailSettings> options,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<SmtpAccountEmailSender> logger) : IAccountEmailSender
{
    private readonly EmailSettings settings = options.Value;

    public Task SendConfirmationLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Confirm your email address", displayName,
            "Please confirm your email address to finish creating your account.", "Confirm email address", link, cancellationToken);

    public Task SendPasswordResetLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Reset your password", displayName,
            "We received a request to reset your password. If it was not you, you can ignore this email.", "Reset password", link, cancellationToken);

    public Task SendAccountExistsNoticeAsync(string email, string displayName, string signInLink, CancellationToken cancellationToken = default) =>
        SendAsync(email, "You already have an account", displayName,
            "Someone tried to create a new account with this email address. You already have an account, so you can sign in or reset your password instead. If it was not you, you can ignore this email.",
            "Sign in", signInLink, cancellationToken);

    private async Task SendAsync(string to, string subject, string displayName, string intro, string action, string link, CancellationToken cancellationToken)
    {
        if (!TryReserveQuota(to))
        {
            // The caller shows the same message either way, so throttling does not reveal anything.
            // The address is not logged to keep personal data out of logs.
            logger.LogWarning("Account email '{Subject}' not sent: per-recipient limit reached", subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var encoder = HtmlEncoder.Default;
        message.Body = new BodyBuilder
        {
            TextBody = $"Hi {displayName},\n\n{intro}\n\n{action}: {link}\n\nThis link expires in a few hours.",
            HtmlBody = $"""
                <p>Hi {encoder.Encode(displayName)},</p>
                <p>{encoder.Encode(intro)}</p>
                <p><a href="{encoder.Encode(link)}">{encoder.Encode(action)}</a></p>
                <p>This link expires in a few hours.</p>
                """,
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(settings.Host, settings.Port, ParseSecurity(settings.Security), cancellationToken);
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            logger.LogInformation("Account email '{Subject}' sent", subject);
        }
        catch (Exception ex) when (ex is MailKit.ProtocolException or MailKit.CommandException or System.Net.Sockets.SocketException or IOException or AuthenticationException or SslHandshakeException)
        {
            // The account action itself succeeded; the user is told to check their inbox and can ask for the
            // email again. The failure is logged for operators (without the recipient's address).
            logger.LogError(ex, "Account email '{Subject}' could not be sent", subject);
        }
    }

    // In-memory counter: good enough for a single instance. With several instances each one counts
    // separately; move the counter to a shared store (SQL, Redis) if that matters.
    private bool TryReserveQuota(string recipient)
    {
        var key = "email-quota:" + recipient.Trim().ToUpperInvariant();
        lock (cache)
        {
            var now = clock.GetUtcNow();
            var sent = cache.Get<List<DateTimeOffset>>(key) ?? [];
            sent.RemoveAll(time => time < now.AddHours(-1));
            if (sent.Count >= settings.MaxPerRecipientPerHour)
            {
                return false;
            }

            sent.Add(now);
            cache.Set(key, sent, TimeSpan.FromHours(1));
            return true;
        }
    }

    private static SecureSocketOptions ParseSecurity(string value) => value switch
    {
        "None" => SecureSocketOptions.None,
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };
}
