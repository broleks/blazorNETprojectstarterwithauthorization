// Prepares a developer machine for both local variants. Run from the repository root:
//
//   dotnet run scripts/setup-local.cs            create what is missing (safe to run again)
//   dotnet run scripts/setup-local.cs -- --force regenerate passwords, client secret and certificates
//
// It creates (all git-ignored, never committed):
//   .env                         passwords and secrets for Docker Compose
//   .certs/localhost.pfx         the ASP.NET Core HTTPS development certificate, for the containers
//   .certs/trust/localhost.crt   public part of that certificate, trusted inside the containers
//   .certs/openiddict-*.pfx      token signing and encryption certificates for Starter.Identity
//   user secrets                 connection strings and secrets for `dotnet run` / Visual Studio
// Nothing in here is suitable for production; see docs/azure-deployment.md.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

var force = args.Contains("--force");
var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "compose.yaml")))
{
    Console.Error.WriteLine("Run this script from the repository root (where compose.yaml is).");
    return 1;
}

var certs = Directory.CreateDirectory(Path.Combine(root, ".certs")).FullName;
Directory.CreateDirectory(Path.Combine(certs, "trust"));

// 1. .env with random secrets ------------------------------------------------------------------
var envPath = Path.Combine(root, ".env");
var env = File.Exists(envPath) && !force ? ReadEnv(envPath) : new Dictionary<string, string>();
string Ensure(string key, Func<string> create) => env.TryGetValue(key, out var v) && v.Length > 0 ? v : env[key] = create();

Ensure("MSSQL_SA_PASSWORD", SqlPassword);
Ensure("SQL_MIGRATOR_PASSWORD", SqlPassword);
Ensure("SQL_IDENTITY_PASSWORD", SqlPassword);
Ensure("SQL_API_PASSWORD", SqlPassword);
Ensure("SQL_WEB_PASSWORD", SqlPassword);
Ensure("WEB_CLIENT_SECRET", () => Secret(32));
Ensure("CERT_PASSWORD", () => Secret(24));
Ensure("GOOGLE_CLIENT_ID", () => "");
Ensure("GOOGLE_CLIENT_SECRET", () => "");
// Production keeps the default of 20 sign-in/registration posts per minute per IP; locally the E2E suite needs more.
Ensure("ACCOUNT_POSTS_PER_MINUTE", () => "200");
WriteEnv(envPath, env);
Console.WriteLine("✔ .env");

// 2. Certificates ------------------------------------------------------------------------------
var certPassword = env["CERT_PASSWORD"];
CreateTokenCertificate(Path.Combine(certs, "openiddict-signing.pfx"), "CN=Starter Identity Signing", X509KeyUsageFlags.DigitalSignature);
CreateTokenCertificate(Path.Combine(certs, "openiddict-encryption.pfx"), "CN=Starter Identity Encryption", X509KeyUsageFlags.KeyEncipherment);
ExportHttpsDevelopmentCertificate();

// 3. User secrets for `dotnet run` (variant 2) -----------------------------------------------
// 127.0.0.1, not "localhost": on Windows "localhost" may resolve to IPv6 (::1) first, while Docker publishes the
// port on IPv4 only, and the connection then waits until it times out.
const string sql = "Server=127.0.0.1,14333;Database=Starter;Encrypt=True;TrustServerCertificate=True";
var migrations = $"{sql};User Id=starter_migrator;Password={env["SQL_MIGRATOR_PASSWORD"]}";
SetUserSecrets("starter-identity-3b7e9a41", new()
{
    ["ConnectionStrings:Default"] = $"{sql};User Id=starter_identity;Password={env["SQL_IDENTITY_PASSWORD"]}",
    ["ConnectionStrings:Migrations"] = migrations,
    ["OpenIddict:Clients:starter-web:ClientSecret"] = env["WEB_CLIENT_SECRET"],
    ["OpenIddict:SigningCertificate:Path"] = Path.Combine(certs, "openiddict-signing.pfx"),
    ["OpenIddict:SigningCertificate:Password"] = certPassword,
    ["OpenIddict:EncryptionCertificate:Path"] = Path.Combine(certs, "openiddict-encryption.pfx"),
    ["OpenIddict:EncryptionCertificate:Password"] = certPassword,
    ["Authentication:Google:ClientId"] = env["GOOGLE_CLIENT_ID"],
    ["Authentication:Google:ClientSecret"] = env["GOOGLE_CLIENT_SECRET"],
});
SetUserSecrets("starter-api-8f1c2d0e", new()
{
    ["ConnectionStrings:Default"] = $"{sql};User Id=starter_api;Password={env["SQL_API_PASSWORD"]}",
    ["ConnectionStrings:Migrations"] = migrations,
});
SetUserSecrets("starter-web-c5d2a7f3", new()
{
    ["ConnectionStrings:Default"] = $"{sql};User Id=starter_web;Password={env["SQL_WEB_PASSWORD"]}",
    ["ConnectionStrings:Migrations"] = migrations,
    ["Authentication:ClientSecret"] = env["WEB_CLIENT_SECRET"],
});
Console.WriteLine("✔ user secrets for Starter.Identity, Starter.Api and Starter.Web");

Console.WriteLine("""

    Next steps:
      Trust the HTTPS development certificate once:   dotnet dev-certs https --trust
      Variant 1 (everything in Docker):               docker compose --profile app up -d --build --wait
      Variant 2 (apps with dotnet run / VS):          docker compose up -d --wait   then run the three projects
    Open https://localhost:7003 and read emails at http://localhost:8025
    """);
return 0;

// ------------------------------------------------------------------------------------------------

static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

// SQL Server's password policy needs upper and lower case letters, digits and a symbol.
static string SqlPassword() => "Sq1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'A').Replace('/', 'b').TrimEnd('=');

static Dictionary<string, string> ReadEnv(string path) =>
    File.ReadAllLines(path)
        .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
        .Select(l => l.Split('=', 2))
        .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

static void WriteEnv(string path, Dictionary<string, string> values) =>
    File.WriteAllLines(path, new[] { "# Local secrets generated by scripts/setup-local.cs. Never commit this file." }
        .Concat(values.Select(kv => $"{kv.Key}={kv.Value}")));

void CreateTokenCertificate(string path, string subject, X509KeyUsageFlags usage)
{
    if (File.Exists(path) && !force)
    {
        Console.WriteLine($"✔ {Path.GetFileName(path)} (existing)");
        return;
    }

    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
    File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, certPassword));
    Console.WriteLine($"✔ {Path.GetFileName(path)}");
}

void ExportHttpsDevelopmentCertificate()
{
    var pfx = Path.Combine(certs, "localhost.pfx");
    // Creates the certificate if the machine does not have one yet; trusting it is a separate, interactive step.
    Run("dotnet", "dev-certs", "https");
    Run("dotnet", "dev-certs", "https", "--export-path", pfx, "--password", certPassword);
    if (!OperatingSystem.IsWindows())
    {
        // The containers run as a non-root user and need to read the (password-protected) file.
        File.SetUnixFileMode(pfx, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    // The containers trust only the public certificate (no private key) for their server-to-server calls.
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(pfx, certPassword);
    File.WriteAllText(Path.Combine(certs, "trust", "localhost.crt"), certificate.ExportCertificatePem());
    Console.WriteLine("✔ localhost.pfx and trust/localhost.crt (HTTPS development certificate)");
}

static void Run(string file, params string[] arguments)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"'{file} {arguments[0]} {arguments[1]}' failed: {process.StandardError.ReadToEnd()}");
    }
}

static void SetUserSecrets(string userSecretsId, Dictionary<string, string> values)
{
    var folder = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", userSecretsId)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", userSecretsId);
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "secrets.json");

    var json = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? [] : [];
    foreach (var (key, value) in values)
    {
        json[key] = value;
    }

    File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
