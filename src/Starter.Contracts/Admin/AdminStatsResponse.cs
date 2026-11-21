namespace Starter.Contracts.Admin;

/// <summary>
/// Application-wide numbers for administrators. Aggregates only: administrators do not get access to the content
/// of other users' data.
/// </summary>
public sealed record AdminStatsResponse(
    int UsersWithNotes,
    int TotalNotes,
    int ArchivedNotes,
    int NotesCreatedLast7Days,
    int NotesUpdatedLast24Hours);
