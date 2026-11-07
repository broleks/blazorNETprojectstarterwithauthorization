using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Starter.Api.Infrastructure;

internal static class DatabaseSetup
{
    /// <summary>SQL Server options shared by the runtime context, migrations and the design-time factory.</summary>
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        // Each application keeps its migration history in its own schema, so the three
        // applications can share one database without stepping on each other.
        sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema);
        // Azure SQL closes idle connections and fails over; retrying transient errors is required there.
        sql.EnableRetryOnFailure();
    }

    public static void AddAppDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured. See docs/configuration.md.");

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, ConfigureSqlServer));
    }

    /// <summary>
    /// Applies migrations using the <c>Migrations</c> connection string, which is the only one allowed to change
    /// the schema. Runs when the app is started with <c>--migrate</c> (a deployment step) or, in Development only,
    /// when <c>Database:MigrateOnStartup</c> is true.
    /// </summary>
    public static async Task MigrateAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Migrations")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No connection string for migrations is configured.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, ConfigureSqlServer)
            .Options;

        await using var db = new AppDbContext(options);
        logger.LogInformation("Applying database migrations for schema '{Schema}'", AppDbContext.Schema);
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied");
    }
}
