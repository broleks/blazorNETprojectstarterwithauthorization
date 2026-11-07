using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Starter.Api.Features.Notes;
using Starter.Api.Infrastructure;
using Starter.Contracts.Dashboard;

namespace Starter.Api.Features.Dashboard;

internal static class DashboardEndpoints
{
    private const int RecentNotesCount = 5;

    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", GetDashboard).WithTags("Dashboard");
        return api;
    }

    private static async Task<DashboardResponse> GetDashboard(ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var mine = db.Notes.AsNoTracking().Where(n => n.OwnerId == ownerId);

        var counts = await mine
            .GroupBy(n => n.IsArchived)
            .Select(g => new { IsArchived = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var recent = await mine
            .Where(n => !n.IsArchived)
            .OrderByDescending(n => n.UpdatedAt)
            .Take(RecentNotesCount)
            .Select(NotesEndpoints.ToSummary)
            .ToListAsync(cancellationToken);

        return new DashboardResponse(
            counts.SingleOrDefault(c => !c.IsArchived)?.Count ?? 0,
            counts.SingleOrDefault(c => c.IsArchived)?.Count ?? 0,
            recent);
    }
}
