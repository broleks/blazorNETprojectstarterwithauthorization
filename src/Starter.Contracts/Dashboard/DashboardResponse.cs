using Starter.Contracts.Notes;

namespace Starter.Contracts.Dashboard;

public sealed record DashboardResponse(int ActiveNotes, int ArchivedNotes, IReadOnlyList<NoteSummary> RecentNotes);
