using Microsoft.EntityFrameworkCore;
using Starter.Api.Infrastructure;
using Starter.Contracts.Admin;

namespace Starter.Api.Features.Admin;

/// <summary>
/// Endpoints for administrators (role Admin). They return aggregates only - administrators do not read other
/// users' notes. User management (lock, roles, delete) lives in Starter.Identity, where the accounts are.
/// </summary>
internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin")
            .RequireAuthorization(AuthenticationSetup.AdminPolicy)
            .WithTags("Admin");

        admin.MapGet("/stats", GetStats);
        return api;
    }

    private static async Task<AdminStatsResponse> GetStats(AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);
        var notes = db.Notes.AsNoTracking();

        return new AdminStatsResponse(
            await notes.Select(n => n.OwnerId).Distinct().CountAsync(cancellationToken),
            await notes.CountAsync(cancellationToken),
            await notes.CountAsync(n => n.IsArchived, cancellationToken),
            await notes.CountAsync(n => n.CreatedAt >= weekAgo, cancellationToken),
            await notes.CountAsync(n => n.UpdatedAt >= dayAgo, cancellationToken));
    }
}
