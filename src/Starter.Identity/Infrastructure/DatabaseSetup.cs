using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenIddict.Abstractions;
using Starter.Contracts;
using Starter.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Starter.Identity.Infrastructure;

internal static class DatabaseSetup
{
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        sql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema);
        sql.EnableRetryOnFailure();
    }

    public static void AddAuthDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured. See docs/configuration.md.");

        builder.Services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlServer(connectionString, ConfigureSqlServer);
            options.UseOpenIddict();
        });
    }

    /// <summary>
    /// Applies migrations with the privileged <c>Migrations</c> connection string, then creates or updates the
    /// OpenIddict client registrations from configuration. Runs as a deployment step (<c>--migrate</c>) and,
    /// in Development only, at startup when <c>Database:MigrateOnStartup</c> is true.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Migrations")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No connection string for migrations is configured.");

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(connectionString, ConfigureSqlServer)
            .UseOpenIddict()
            .Options;

        await using (var db = new AuthDbContext(options))
        {
            logger.LogInformation("Applying database migrations for schema '{Schema}'", AuthDbContext.Schema);
            await db.Database.MigrateAsync(cancellationToken);
        }

        await using var scope = services.CreateAsyncScope();
        await SeedClientsAsync(scope.ServiceProvider, configuration, logger, cancellationToken);
        await SeedAdministratorsAsync(scope.ServiceProvider, configuration, logger);
    }

    /// <summary>
    /// Creates the Admin role and gives it to the accounts listed in <c>Admin:InitialAdmins</c> (email addresses).
    /// Only existing accounts with a confirmed email are promoted; nothing is ever removed here. Later
    /// administrators are appointed on the Administration pages.
    /// </summary>
    private static async Task SeedAdministratorsAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(ApiConstants.AdminRole))
        {
            await roles.CreateAsync(new IdentityRole(ApiConstants.AdminRole));
        }

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var email in (configuration.GetSection("Admin:InitialAdmins").Get<string[]>() ?? []).Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var user = await users.FindByEmailAsync(email);
            if (user is not { EmailConfirmed: true })
            {
                logger.LogWarning("Initial administrator {Email} not found or email not confirmed; skipped", ActivityLogExtensions.MaskEmail(email));
                continue;
            }

            if (!await users.IsInRoleAsync(user, ApiConstants.AdminRole))
            {
                await users.AddToRoleAsync(user, ApiConstants.AdminRole);
                logger.LogActivity(LogCategories.Security, "admin.role_granted", EventOutcome.Success,
                    new Dictionary<string, object?> { ["GrantedBy"] = "configuration" }, user.Id);
            }
        }
    }

    private static async Task SeedClientsAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        var settings = configuration.GetSection(OpenIddictSettings.SectionName).Get<OpenIddictSettings>() ?? new OpenIddictSettings();
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (clientId, client) in settings.Clients)
        {
            var descriptor = CreateDescriptor(clientId, client);
            var existing = await manager.FindByClientIdAsync(clientId, cancellationToken);
            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancellationToken);
                logger.LogInformation("Created OpenIddict client {ClientId}", clientId);
            }
            else
            {
                // Keeps redirect URIs and secrets in sync with configuration (e.g. after a secret rotation).
                await manager.UpdateAsync(existing, descriptor, cancellationToken);
                logger.LogInformation("Updated OpenIddict client {ClientId}", clientId);
            }
        }
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(string clientId, ClientSettings client)
    {
        if (client.Kind == ClientKind.Service)
        {
            if (string.IsNullOrWhiteSpace(client.ClientSecret))
            {
                throw new InvalidOperationException($"Service client '{clientId}' needs a ClientSecret.");
            }

            return new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = client.ClientSecret,
                ClientType = ClientTypes.Confidential,
                DisplayName = client.DisplayName,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + ApiConstants.AccountsScope,
                },
            };
        }

        var isConfidential = !string.IsNullOrWhiteSpace(client.ClientSecret);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = isConfidential ? client.ClientSecret : null,
            ClientType = isConfidential ? ClientTypes.Confidential : ClientTypes.Public,
            DisplayName = client.DisplayName,
            // First-party clients: the user is not asked for consent. Third-party clients would need a consent screen.
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + ApiConstants.Scope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        foreach (var uri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        return descriptor;
    }
}
