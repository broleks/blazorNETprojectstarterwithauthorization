using Microsoft.EntityFrameworkCore;
using Starter.Api.Features.UserData;
using Starter.Api.Infrastructure;

namespace Starter.Api.Features.Notes;

/// <summary>Export and deletion of a user's notes (GDPR). The pattern every feature with user data follows.</summary>
internal sealed class NotesUserData(AppDbContext db) : IUserDataHandler
{
    public string Name => "notes";

    public IReadOnlyCollection<Type> Entities => [typeof(Note)];

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken) =>
        await db.Notes.AsNoTracking()
            .Where(n => n.OwnerId == userId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Title, n.Content, n.IsArchived, n.CreatedAt, n.UpdatedAt })
            .ToListAsync(cancellationToken);

    public Task DeleteAsync(string userId, CancellationToken cancellationToken) =>
        db.Notes.Where(n => n.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
}
