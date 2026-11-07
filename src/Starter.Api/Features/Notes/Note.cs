using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Contracts.Notes;

namespace Starter.Api.Features.Notes;

/// <summary>
/// Database model of a note. It is never returned from the API directly: endpoints map it to the
/// contract types in Starter.Contracts so the database shape can change without breaking clients.
/// </summary>
public sealed class Note
{
    public Guid Id { get; set; }

    /// <summary>Subject (<c>sub</c>) of the access token that created the note. Set only by the API.</summary>
    public required string OwnerId { get; set; }

    public required string Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public const int OwnerIdMaxLength = 64;

    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes", table =>
        {
            // Defence in depth: the API validates input, the database refuses obviously broken rows.
            table.HasCheckConstraint("CK_Notes_Title_NotBlank", "LEN(LTRIM(RTRIM([Title]))) > 0");
            table.HasCheckConstraint("CK_Notes_OwnerId_NotBlank", "LEN([OwnerId]) > 0");
        });

        builder.HasKey(n => n.Id);
        builder.Property(n => n.OwnerId).HasMaxLength(OwnerIdMaxLength).IsUnicode(false);
        builder.Property(n => n.Title).HasMaxLength(NoteLimits.TitleMaxLength);
        builder.Property(n => n.Content).HasMaxLength(NoteLimits.ContentMaxLength);

        // Every query filters by owner and archive state and sorts by last update,
        // so one composite index serves the list, the dashboard counters and "recent notes".
        builder.HasIndex(n => new { n.OwnerId, n.IsArchived, n.UpdatedAt })
            .HasDatabaseName("IX_Notes_Owner_Archived_Updated")
            .IsDescending(false, false, true);
    }
}
