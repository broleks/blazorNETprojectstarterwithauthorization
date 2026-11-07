using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Starter.Web.Infrastructure.Sessions;

/// <summary>
/// A signed-in browser session, stored on the server. The browser cookie contains only <see cref="Key"/>.
/// The authentication ticket and the OAuth tokens are encrypted with ASP.NET Core Data Protection before
/// they are written, and they are stored separately so a cookie renewal cannot overwrite freshly refreshed tokens.
/// </summary>
public sealed class WebSession
{
    public required string Key { get; set; }

    /// <summary>The <c>sub</c> of the user, to find all sessions of one user.</summary>
    public required string UserId { get; set; }

    public required byte[] Ticket { get; set; }
    public required byte[] Tokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class WebSessionConfiguration : IEntityTypeConfiguration<WebSession>
{
    public void Configure(EntityTypeBuilder<WebSession> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(64).IsUnicode(false);
        builder.Property(s => s.UserId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.ExpiresAt);
    }
}

/// <summary>
/// Starter.Web's own small database area (schema <c>web</c>): sessions and Data Protection keys.
/// The web app has no access to business data or to the identity tables; it reads those through the API
/// and the identity server.
/// </summary>
public sealed class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "web";

    public DbSet<WebSession> Sessions => Set<WebSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new WebSessionConfiguration());
    }
}

internal sealed class WebDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WebDbContext>
{
    public WebDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WebDbContext>()
            .UseSqlServer("Server=design-time-only", DatabaseSetup.ConfigureSqlServer)
            .Options);
}
