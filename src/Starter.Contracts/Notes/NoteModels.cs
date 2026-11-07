using System.ComponentModel.DataAnnotations;

namespace Starter.Contracts.Notes;

public sealed record NoteSummary(Guid Id, string Title, string Preview, bool IsArchived, DateTimeOffset UpdatedAt);

public sealed record NoteDetails(
    Guid Id,
    string Title,
    string Content,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Body of <c>POST /api/notes</c>. There is deliberately no owner field: the API takes the owner
/// from the validated access token, and unknown JSON properties are rejected.
/// </summary>
public sealed class CreateNoteRequest
{
    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(NoteLimits.TitleMaxLength, ErrorMessage = "The title can be at most {1} characters long.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(NoteLimits.ContentMaxLength, ErrorMessage = "The note can be at most {1} characters long.")]
    public string? Content { get; set; }
}

/// <summary>Body of <c>PUT /api/notes/{id}</c>. Archiving and restoring is done by changing <see cref="IsArchived"/>.</summary>
public sealed class UpdateNoteRequest
{
    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(NoteLimits.TitleMaxLength, ErrorMessage = "The title can be at most {1} characters long.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(NoteLimits.ContentMaxLength, ErrorMessage = "The note can be at most {1} characters long.")]
    public string? Content { get; set; }

    public bool IsArchived { get; set; }
}
