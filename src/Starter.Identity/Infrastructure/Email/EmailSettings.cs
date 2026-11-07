namespace Starter.Identity.Infrastructure.Email;

/// <summary>Configuration section "Email". Locally it points at Mailpit; in Azure at any SMTP relay (e.g. Azure Communication Services).</summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public string FromAddress { get; init; } = "no-reply@starter.local";
    public string FromName { get; init; } = "Starter";
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;

    /// <summary>None (local Mailpit only), StartTls or SslOnConnect. Certificate validation is always on.</summary>
    public string Security { get; init; } = "StartTls";
    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary>Maximum number of account emails sent to one address per hour (protects against mail bombing).</summary>
    public int MaxPerRecipientPerHour { get; init; } = 5;
}
