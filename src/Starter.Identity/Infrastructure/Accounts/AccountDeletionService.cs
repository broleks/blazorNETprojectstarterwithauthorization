using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Infrastructure.Accounts;

/// <summary>
/// Deletes an account immediately (GDPR "right to erasure"), whether the user asked for it or an administrator did.
///
/// 1. All refresh tokens are revoked, so no app can get new access tokens for the account.
/// 2. In one transaction: the account is deleted and a <see cref="PendingDataDeletion"/> row is written.
/// 3. OpenIddict token and authorization entries of the user are removed, a confirmation email is sent.
/// 4. <see cref="PendingDeletionProcessor"/> deletes the user's data in the API in the background and retries until
///    the API confirms - so the deletion completes even if the API is down at that moment.
/// </summary>
public sealed class AccountDeletionService(
    AuthDbContext db,
    UserManager<ApplicationUser> users,
    AccountSecurity accountSecurity,
    IOpenIddictTokenManager tokens,
    IOpenIddictAuthorizationManager authorizations,
    IAccountEmailSender emailSender,
    PendingDeletionSignal signal,
    TimeProvider clock,
    ILogger<AccountDeletionService> logger)
{
    /// <param name="initiatedBy">"self", or the user id of the administrator who deleted the account.</param>
    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, string initiatedBy, CancellationToken cancellationToken)
    {
        var (userId, email, displayName) = (user.Id, user.Email, user.DisplayName);
        await accountSecurity.RevokeAllSessionsAsync(user, cancellationToken);

        var result = IdentityResult.Success;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await db.PendingDataDeletions.AnyAsync(d => d.UserId == userId, cancellationToken))
            {
                db.PendingDataDeletions.Add(new PendingDataDeletion { UserId = userId, RequestedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(cancellationToken);
            }

            result = await users.DeleteAsync(user); // uses the same DbContext, so it is part of the transaction
            if (result.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });

        if (!result.Succeeded)
        {
            logger.LogActivity(LogCategories.Security, "account.delete", EventOutcome.Failure, new Dictionary<string, object?>
            {
                ["InitiatedBy"] = initiatedBy,
                ["Reason"] = string.Join(",", result.Errors.Select(e => e.Code)),
            }, userId);
            return result;
        }

        // The entries are already revoked; remove them because they still carry the user id.
        foreach (var token in await ListAsync(tokens.FindBySubjectAsync(userId, cancellationToken)))
        {
            await tokens.DeleteAsync(token, cancellationToken);
        }

        foreach (var authorization in await ListAsync(authorizations.FindBySubjectAsync(userId, cancellationToken)))
        {
            await authorizations.DeleteAsync(authorization, cancellationToken);
        }

        logger.LogActivity(LogCategories.Security, "account.delete", EventOutcome.Success,
            new Dictionary<string, object?> { ["InitiatedBy"] = initiatedBy }, userId);

        if (email is not null)
        {
            await emailSender.SendAccountDeletedNoticeAsync(email, displayName, cancellationToken);
        }

        signal.Trigger();
        return result;
    }

    private static async Task<List<object>> ListAsync(IAsyncEnumerable<object> source)
    {
        var list = new List<object>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
