using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Starter.Identity.Infrastructure;

namespace Starter.Identity.Data;

/// <summary>
/// Accounts (ASP.NET Core Identity), OpenIddict applications/authorizations/tokens and this app's
/// Data Protection keys. Everything lives in the <c>identity</c> schema, which only Starter.Identity can access.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<PendingDataDeletion> PendingDataDeletions => Set<PendingDataDeletion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        });

        builder.Entity<PendingDataDeletion>(deletion =>
        {
            deletion.HasKey(d => d.UserId);
            deletion.Property(d => d.UserId).HasMaxLength(450);
            deletion.Property(d => d.LastError).HasMaxLength(1000);
        });

        // Registers the OpenIddict entities (applications, authorizations, scopes, tokens).
        builder.UseOpenIddict();
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> create the context without starting the web host.</summary>
internal sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=design-time-only", DatabaseSetup.ConfigureSqlServer)
            .Options);
}
