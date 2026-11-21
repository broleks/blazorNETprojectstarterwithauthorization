using Microsoft.AspNetCore.Http.HttpResults;
using Starter.Api.Infrastructure;
using Starter.Observability;

namespace Starter.Api.Features.UserData;

/// <summary>
/// Internal endpoints called by Starter.Identity (service client, accounts scope) when a user exports their data or
/// their account is deleted. Not reachable with a user's token.
/// </summary>
internal static class UserDataEndpoints
{
    public static void MapUserDataEndpoints(this IEndpointRouteBuilder app)
    {
        var internalApi = app.MapGroup("/api/internal/users/{userId}")
            .RequireAuthorization(AuthenticationSetup.AccountsServicePolicy)
            .WithTags("Internal");

        internalApi.MapGet("/export", Export);
        internalApi.MapDelete("/", Delete);
    }

    private static async Task<Ok<Dictionary<string, object>>> Export(
        string userId, IEnumerable<IUserDataHandler> handlers, CancellationToken cancellationToken)
    {
        var export = new Dictionary<string, object>();
        foreach (var handler in handlers)
        {
            export[handler.Name] = await handler.ExportAsync(userId, cancellationToken);
        }

        return TypedResults.Ok(export);
    }

    private static async Task<NoContent> Delete(
        string userId, IEnumerable<IUserDataHandler> handlers, ILogger<UserDataHandlerLog> logger, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler.DeleteAsync(userId, cancellationToken);
        }

        logger.LogActivity(LogCategories.Security, "account.data_deleted", EventOutcome.Success,
            new Dictionary<string, object?> { ["Features"] = string.Join(",", handlers.Select(h => h.Name)) }, userId);
        return TypedResults.NoContent();
    }
}

/// <summary>Logger category for user data deletion.</summary>
internal sealed class UserDataHandlerLog;
