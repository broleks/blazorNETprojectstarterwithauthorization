using Microsoft.Extensions.Logging;

namespace Starter.Observability;

/// <summary>Names of the log categories. An event belongs to at most one category.</summary>
public static class LogCategories
{
    public const string Security = "Security";
    public const string Api = "Api";
    public const string Frontend = "Frontend";
}

/// <summary>Names of the structured properties that are mapped to Elastic Common Schema fields.</summary>
public static class LogProperties
{
    public const string Category = "LogCategory";        // labels.LogCategory
    public const string Action = "EventAction";          // event.action
    public const string Outcome = "EventOutcome";        // event.outcome
    public const string UserId = "UserId";               // user.id
    public const string SessionId = "SessionId";         // session id (a hash, never the session key itself)
    public const string DurationMs = "DurationMs";
}

public enum EventOutcome
{
    Success,
    Failure,
    Unknown,
}

/// <summary>
/// Writes a named, structured event, e.g. <c>logger.LogActivity(LogCategories.Security, "auth.sign_in", EventOutcome.Failure)</c>.
/// Use stable dotted action names - they are what you search for in Kibana (<c>event.action</c>).
/// Never pass passwords, tokens, codes or user-entered content in <paramref name="details"/>.
/// </summary>
public static class ActivityLogExtensions
{
    public static void LogActivity(
        this ILogger logger,
        string category,
        string action,
        EventOutcome outcome,
        IReadOnlyDictionary<string, object?>? details = null,
        string? userId = null,
        LogLevel? level = null,
        Exception? exception = null)
    {
        var properties = new Dictionary<string, object?>(details ?? new Dictionary<string, object?>())
        {
            [LogProperties.Category] = category,
        };
        if (userId is not null)
        {
            properties[LogProperties.UserId] = userId;
        }

        using (logger.BeginScope(properties))
        {
            logger.Log(
                level ?? (outcome == EventOutcome.Failure ? LogLevel.Warning : LogLevel.Information),
                exception,
                "{EventAction:l} {EventOutcome:l}",
                action,
                outcome.ToString().ToLowerInvariant());
        }
    }

    /// <summary>"lukasz@example.com" → "l***@example.com". For failed sign-ins, where no user id is known.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : email[0] + "***" + email[at..];
    }

    /// <summary>A short, stable, non-reversible id for a secret value (e.g. a session key), safe to log.</summary>
    public static string HashForLogs(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}
