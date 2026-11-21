namespace Starter.Identity.Data;

/// <summary>
/// A deleted account whose data in the API still has to be deleted (or deleted once more, see
/// <see cref="Infrastructure.Accounts.PendingDeletionProcessor"/>). Holds only the user id, no other personal data.
/// </summary>
public sealed class PendingDataDeletion
{
    public required string UserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
