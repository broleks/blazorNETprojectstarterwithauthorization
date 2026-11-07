using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Email;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Starter.Api.Tests.Infrastructure.StarterFixture))]

namespace Starter.Api.Tests.Infrastructure;

/// <summary>
/// Starts everything once per test run:
/// <list type="bullet">
/// <item>SQL Server in a container (or the server in <c>STARTER_TEST_SQL</c>, e.g. in CI with a service container),</item>
/// <item>the real Starter.Identity (OpenIddict, ASP.NET Core Identity) on https://identity.test,</item>
/// <item>the real Starter.Api on https://api.test, validating tokens against that identity server.</item>
/// </list>
/// Nothing in the authentication configuration is replaced: the API downloads the signing keys from the identity
/// server's discovery document, exactly as in production. Only the email sender is swapped for an in-memory one.
/// </summary>
public sealed class StarterFixture : IAsyncLifetime
{
    public const string IdentityUrl = "https://identity.test/";
    public const string ApiUrl = "https://api.test/";
    public const string WebRedirectUri = "https://web.test/signin-oidc";
    public const string ClientId = "starter-web";
    public const string ClientSecret = "integration-test-client-secret";
    public const string CertificatePassword = "integration-tests";

    private readonly string certificateFolder = Directory.CreateTempSubdirectory("starter-tests-").FullName;
    private MsSqlContainer? container;

    public string ConnectionString { get; private set; } = string.Empty;
    public X509Certificate2 SigningCertificate { get; private set; } = default!;
    public CapturingEmailSender Emails { get; } = new();
    public IdentityFactory Identity { get; private set; } = default!;
    public ApiFactory Api { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await CreateDatabaseAsync();

        SigningCertificate = CreateCertificate("signing.pfx", "CN=Test Signing", X509KeyUsageFlags.DigitalSignature);
        CreateCertificate("encryption.pfx", "CN=Test Encryption", X509KeyUsageFlags.KeyEncipherment);

        Identity = new IdentityFactory(this, configure: null);
        await Starter.Identity.Infrastructure.DatabaseSetup.MigrateAndSeedAsync(
            Identity.Services, Identity.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), NullLogger.Instance);

        Api = new ApiFactory(this, settings: null);
        await Starter.Api.Infrastructure.DatabaseSetup.MigrateAsync(
            Api.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), NullLogger.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await Identity.DisposeAsync();
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        Directory.Delete(certificateFolder, recursive: true);
    }

    /// <summary>Settings shared by every identity server instance used in the tests.</summary>
    public Dictionary<string, string?> IdentitySettings() => new()
    {
        ["ConnectionStrings:Default"] = ConnectionString,
        ["Authentication:Issuer"] = IdentityUrl,
        ["Authentication:DefaultReturnUrl"] = "https://web.test/",
        ["OpenIddict:SigningCertificate:Path"] = Path.Combine(certificateFolder, "signing.pfx"),
        ["OpenIddict:SigningCertificate:Password"] = CertificatePassword,
        ["OpenIddict:EncryptionCertificate:Path"] = Path.Combine(certificateFolder, "encryption.pfx"),
        ["OpenIddict:EncryptionCertificate:Password"] = CertificatePassword,
        ["OpenIddict:Clients:starter-web:DisplayName"] = "Starter Web (tests)",
        ["OpenIddict:Clients:starter-web:ClientSecret"] = ClientSecret,
        ["OpenIddict:Clients:starter-web:RedirectUris:0"] = WebRedirectUri,
        ["OpenIddict:Clients:starter-web:PostLogoutRedirectUris:0"] = "https://web.test/signout-callback-oidc",
        // Every test signs in through the login form; the per-IP limit is tested separately.
        ["RateLimiting:AccountPostsPerMinute"] = "10000",
    };

    /// <summary>Creates a confirmed account directly in the database (the registration UI is covered by the E2E tests).</summary>
    public async Task<TestUser> CreateUserAsync(string? displayName = null)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "integration test password";
        await using var scope = Identity.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName ?? "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return new TestUser(user.Id, email, password);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var databaseName = "StarterTests_" + Guid.NewGuid().ToString("N")[..8];
        var external = Environment.GetEnvironmentVariable("STARTER_TEST_SQL");
        string serverConnectionString;
        if (!string.IsNullOrWhiteSpace(external))
        {
            serverConnectionString = external;
        }
        else
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await container.StartAsync();
            serverConnectionString = container.GetConnectionString();
        }

        // Each run uses a new database so runs never share data. As in the real deployment the database exists
        // before the migrations run (they only create schemas and tables).
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(serverConnectionString) { TrustServerCertificate = true };
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        builder.InitialCatalog = databaseName;
        return builder.ConnectionString;
    }

    private X509Certificate2 CreateCertificate(string fileName, string subject, X509KeyUsageFlags usage)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var bytes = certificate.Export(X509ContentType.Pfx, CertificatePassword);
        File.WriteAllBytes(Path.Combine(certificateFolder, fileName), bytes);
        return X509CertificateLoader.LoadPkcs12(bytes, CertificatePassword);
    }
}

public sealed record TestUser(string Id, string Email, string Password);

/// <summary>The real Starter.Identity, hosted in memory.</summary>
public sealed class IdentityFactory : WebApplicationFactory<ApplicationUser>
{
    private readonly StarterFixture fixture;
    private readonly Dictionary<string, string?>? configure;

    public IdentityFactory(StarterFixture fixture, Dictionary<string, string?>? configure)
    {
        this.fixture = fixture;
        this.configure = configure;
        // The identity server only accepts HTTPS and issues tokens for this exact address.
        ClientOptions.BaseAddress = new Uri(StarterFixture.IdentityUrl);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in fixture.IdentitySettings().Concat(configure ?? []))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IAccountEmailSender>(fixture.Emails);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.BaseAddress = new Uri(StarterFixture.IdentityUrl);
    }

    /// <summary>A browser-like client: keeps cookies, does not follow redirects automatically.</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri(StarterFixture.IdentityUrl),
    });
}

/// <summary>The real Starter.Api, hosted in memory. Its token validation talks to the in-memory identity server.</summary>
public sealed class ApiFactory : WebApplicationFactory<Starter.Api.Infrastructure.AppDbContext>
{
    private readonly StarterFixture fixture;
    private readonly Dictionary<string, string?>? settings;

    public ApiFactory(StarterFixture fixture, Dictionary<string, string?>? settings)
    {
        this.fixture = fixture;
        this.settings = settings;
        ClientOptions.BaseAddress = new Uri(StarterFixture.ApiUrl);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", fixture.ConnectionString);
        builder.UseSetting("Authentication:Issuer", StarterFixture.IdentityUrl);
        foreach (var (key, value) in settings ?? [])
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // Route the API's outgoing HTTP calls (discovery document and signing keys) to the in-memory identity server.
            // OpenIddict insists on HttpClientHandler as the primary handler, so the redirection is an extra handler.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler =>
                    handler.AdditionalHandlers.Add(new ForwardToTestServerHandler(fixture.Identity.Server.CreateHandler()))));
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.BaseAddress = new Uri(StarterFixture.ApiUrl);
    }
}

/// <summary>Sends requests to an in-memory test server instead of the network.</summary>
internal sealed class ForwardToTestServerHandler(HttpMessageHandler testServerHandler) : DelegatingHandler
{
    private readonly HttpMessageInvoker invoker = new(testServerHandler);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        invoker.SendAsync(request, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            invoker.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Records account emails instead of sending them.</summary>
public sealed class CapturingEmailSender : IAccountEmailSender
{
    private readonly List<(string To, string Subject, string Link)> sent = [];

    public IReadOnlyList<(string To, string Subject, string Link)> Sent
    {
        get
        {
            lock (sent)
            {
                return [.. sent];
            }
        }
    }

    public Task SendConfirmationLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default) => Add(email, "confirm", link);

    public Task SendPasswordResetLinkAsync(string email, string displayName, string link, CancellationToken cancellationToken = default) => Add(email, "reset", link);

    public Task SendAccountExistsNoticeAsync(string email, string displayName, string signInLink, CancellationToken cancellationToken = default) => Add(email, "exists", signInLink);

    private Task Add(string to, string subject, string link)
    {
        lock (sent)
        {
            sent.Add((to, subject, link));
        }

        return Task.CompletedTask;
    }
}
