namespace Starter.E2E.Tests.Infrastructure;

/// <summary>
/// Where the application under test runs and how the browser is started. Every value can be overridden with an
/// environment variable, so the same tests run against Docker Compose, `dotnet run`, or a CI environment.
/// The defaults match the local setup described in the README.
/// </summary>
public sealed record E2ESettings
{
    public Uri WebUrl { get; init; } = Read("E2E_WEB_URL", new Uri("https://localhost:7003/"));
    public Uri IdentityUrl { get; init; } = Read("E2E_IDENTITY_URL", new Uri("https://localhost:7001/"));
    public Uri MailpitUrl { get; init; } = Read("E2E_MAILPIT_URL", new Uri("http://localhost:8025/"));

    /// <summary>chromium (default), firefox or webkit.</summary>
    public string Browser { get; init; } = Environment.GetEnvironmentVariable("E2E_BROWSER") ?? "chromium";

    /// <summary>Optional path to an already installed browser (e.g. a system Chrome) instead of Playwright's download.</summary>
    public string? BrowserExecutable { get; init; } = Environment.GetEnvironmentVariable("E2E_BROWSER_EXECUTABLE");

    public bool Headless { get; init; } = Environment.GetEnvironmentVariable("E2E_HEADED") is not ("1" or "true");

    /// <summary>
    /// Only for throw-away environments (CI) whose browser does not trust the local development certificate.
    /// Locally, trust the certificate instead (dotnet dev-certs https --trust).
    /// </summary>
    public bool IgnoreHttpsErrors { get; init; } = Environment.GetEnvironmentVariable("E2E_IGNORE_HTTPS_ERRORS") is "1" or "true";

    /// <summary>
    /// Optional SQL Server connection string of the test environment. When set, data created by the tests
    /// (accounts with the test email domain and their notes) is deleted after the run.
    /// </summary>
    public string? CleanupConnectionString { get; init; } = Environment.GetEnvironmentVariable("E2E_SQL");

    /// <summary>Folder for screenshots and traces of failed tests (never committed: see .gitignore).</summary>
    public string ArtifactsFolder { get; init; } =
        Environment.GetEnvironmentVariable("E2E_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "playwright");

    /// <summary>Every account created by the tests uses this domain, so test data is easy to find and remove.</summary>
    public const string EmailDomain = "e2e.starter.test";

    public static E2ESettings Current { get; } = new();

    private static Uri Read(string name, Uri fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? new Uri(value.EndsWith('/') ? value : value + "/") : fallback;
}
