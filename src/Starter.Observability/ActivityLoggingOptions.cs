namespace Starter.Observability;

/// <summary>
/// Configuration section "ActivityLogging", the same in every application.
/// Categories and <see cref="Enabled"/> are re-read while the application runs (no restart needed);
/// <see cref="Destination"/>, the file path, retention and the Elasticsearch connection need a restart.
/// </summary>
public sealed class ActivityLoggingOptions
{
    public const string SectionName = "ActivityLogging";

    /// <summary>Master switch. False: nothing is written to files or Elasticsearch (console output is not affected).</summary>
    public bool Enabled { get; set; } = true;

    public LogDestination Destination { get; set; } = LogDestination.File;

    /// <summary>How long local log files are kept. Elasticsearch retention is set by the index template (see docs/logging.md).</summary>
    public int RetentionDays { get; set; } = 7;

    public LogCategoryOptions Categories { get; set; } = new();
    public FileLogOptions File { get; set; } = new();
    public ElasticsearchLogOptions Elasticsearch { get; set; } = new();
}

public enum LogDestination
{
    File,
    Elasticsearch,
    Both,
}

/// <summary>What is stored. Each switch is independent; see docs/logging.md for ready-made combinations.</summary>
public sealed class LogCategoryOptions
{
    /// <summary>Warnings, errors and exceptions from any part of the application.</summary>
    public bool Errors { get; set; } = true;

    /// <summary>Sign-in, sign-out, registration, email confirmation, password changes, lockouts, Google.</summary>
    public bool Security { get; set; } = true;

    /// <summary>Every API call, logged by the caller (Web) and by the API, with status, duration and error details.</summary>
    public bool Api { get; set; } = true;

    /// <summary>Page views and UI actions in the Blazor app. Verbose; switch on while investigating.</summary>
    public bool Frontend { get; set; }
}

public sealed class FileLogOptions
{
    /// <summary>Root folder; each application writes to &lt;Path&gt;/&lt;service&gt;/&lt;service&gt;-&lt;date&gt;.json.</summary>
    public string Path { get; set; } = "logs";
}

public sealed class ElasticsearchLogOptions
{
    /// <summary>For example http://localhost:9200 (local Docker) or the Elastic Cloud endpoint.</summary>
    public string? Url { get; set; }

    /// <summary>Secret. Leave empty for the local, unsecured development cluster.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Data stream namespace, usually the environment (development, test, production).</summary>
    public string Namespace { get; set; } = "development";
}
