using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Starter.Api.Infrastructure;
using Starter.Contracts;
using Starter.Contracts.Notes;

namespace Starter.Api.Features.Notes;

/// <summary>
/// The private notes module. Use it as the pattern for new features: one folder with the entity,
/// its EF configuration and its endpoints; contracts in Starter.Contracts; no extra layers.
///
/// Ownership rule: every query is filtered by the caller's user id taken from the access token.
/// A note owned by someone else is indistinguishable from a note that does not exist (404),
/// so the API does not reveal which ids exist.
/// </summary>
internal static class NotesEndpoints
{
    public static RouteGroupBuilder MapNotesEndpoints(this RouteGroupBuilder api)
    {
        var notes = api.MapGroup("/notes").WithTags("Notes");

        notes.MapGet("/", ListNotes);
        notes.MapGet("/{id:guid}", GetNote).WithName(nameof(GetNote));
        notes.MapPost("/", CreateNote).RequireRateLimiting(RateLimitingSetup.WritePolicy);
        notes.MapPut("/{id:guid}", UpdateNote).RequireRateLimiting(RateLimitingSetup.WritePolicy);
        notes.MapDelete("/{id:guid}", DeleteNote).RequireRateLimiting(RateLimitingSetup.WritePolicy);

        return api;
    }

    internal sealed record ListNotesQuery(
        bool Archived = false,
        [property: Range(1, 100_000)] int Page = 1,
        [property: Range(1, NoteLimits.MaxPageSize)] int PageSize = NoteLimits.DefaultPageSize);

    private static async Task<Ok<PagedResponse<NoteSummary>>> ListNotes(
        [AsParameters] ListNotesQuery query, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var notes = db.Notes.AsNoTracking().Where(n => n.OwnerId == ownerId && n.IsArchived == query.Archived);

        var total = await notes.CountAsync(cancellationToken);
        var items = await notes
            .OrderByDescending(n => n.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<NoteSummary>(items, query.Page, query.PageSize, total));
    }

    private static async Task<Results<Ok<NoteDetails>, NotFound>> GetNote(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var note = await FindOwnedNote(db, id, user, cancellationToken);
        return note is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetails(note));
    }

    private static async Task<CreatedAtRoute<NoteDetails>> CreateNote(
        CreateNoteRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var note = new Note
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = user.GetUserId(), // never taken from the request
            Title = request.Title.Trim(),
            Content = request.Content ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.CreatedAtRoute(ToDetails(note), nameof(GetNote), new { id = note.Id });
    }

    private static async Task<Results<Ok<NoteDetails>, NotFound>> UpdateNote(
        Guid id, UpdateNoteRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var note = await FindOwnedNote(db, id, user, cancellationToken);
        if (note is null)
        {
            return TypedResults.NotFound();
        }

        note.Title = request.Title.Trim();
        note.Content = request.Content ?? string.Empty;
        note.IsArchived = request.IsArchived;
        note.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToDetails(note));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteNote(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        // The owner condition is part of the DELETE statement itself, not a separate check.
        var deleted = await db.Notes.Where(n => n.Id == id && n.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static Task<Note?> FindOwnedNote(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        return db.Notes.SingleOrDefaultAsync(n => n.Id == id && n.OwnerId == ownerId, cancellationToken);
    }

    private const int PreviewLength = 160;

    // Expression so that EF Core translates it to SQL and only the preview is read from the database.
#pragma warning disable CA1845 // Span-based APIs cannot be translated to SQL.
    internal static readonly System.Linq.Expressions.Expression<Func<Note, NoteSummary>> ToSummary = n => new NoteSummary(
        n.Id,
        n.Title,
        n.Content.Length > PreviewLength ? n.Content.Substring(0, PreviewLength) + "…" : n.Content,
        n.IsArchived,
        n.UpdatedAt);
#pragma warning restore CA1845

    private static NoteDetails ToDetails(Note note) =>
        new(note.Id, note.Title, note.Content, note.IsArchived, note.CreatedAt, note.UpdatedAt);
}
