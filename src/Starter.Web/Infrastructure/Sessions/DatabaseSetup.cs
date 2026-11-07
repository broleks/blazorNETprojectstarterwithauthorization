using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Starter.Web.Infrastructure.Sessions;

internal static class DatabaseSetup
{
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        sql.MigrationsHistoryTable("__EFMigrationsHistory", WebDbContext.Schema);
        sql.EnableRetryOnFailure();
    }

    public static void AddWebDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured. See docs/configuration.md.");

        // A factory because the session store and token service are singletons used from many circuits.
        builder.Services.AddDbContextFactory<WebDbContext>(options => options.UseSqlServer(connectionString, ConfigureSqlServer));
    }

    /// <summary>Applies migrations with the privileged <c>Migrations</c> connection string (deployment step or Development startup).</summary>
    public static async Task MigrateAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Migrations")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No connection string for migrations is configured.");

        await using var db = new WebDbContext(new DbContextOptionsBuilder<WebDbContext>()
            .UseSqlServer(connectionString, ConfigureSqlServer)
            .Options);

        logger.LogInformation("Applying database migrations for schema '{Schema}'", WebDbContext.Schema);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
