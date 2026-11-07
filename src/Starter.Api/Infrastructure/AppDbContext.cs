using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Api.Features.Notes;

namespace Starter.Api.Infrastructure;

/// <summary>
/// Business data of the application. It lives in the <c>app</c> schema, which the API's runtime
/// database user can read and write. Identity data lives in other schemas the API cannot access.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string Schema = "app";

    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> create the context without starting the web host.</summary>
internal sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=design-time-only", DatabaseSetup.ConfigureSqlServer)
            .Options);
}
