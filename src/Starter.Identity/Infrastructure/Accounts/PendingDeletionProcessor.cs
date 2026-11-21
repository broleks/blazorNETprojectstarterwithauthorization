using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Starter.Identity.Data;

namespace Starter.Identity.Infrastructure.Accounts;

/// <summary>Wakes the processor right after an account was deleted instead of waiting for the next minute.</summary>
public sealed class PendingDeletionSignal : IDisposable
{
    private readonly SemaphoreSlim signal = new(0);

    public void Dispose() => signal.Dispose();

    public void Trigger() => signal.Release();

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// Deletes the API data of deleted accounts (see <see cref="AccountDeletionService"/>), retrying every minute.
///
/// An access token issued before the deletion stays valid until it expires, so a browser tab that was still open
/// could store new data for the deleted user in that window. Each deletion is therefore repeated until the
/// access token lifetime (plus a margin) has passed; only then the pending row is removed.
/// </summary>
public sealed class PendingDeletionProcessor(
    IServiceScopeFactory scopes,
    ApiServiceClient api,
    PendingDeletionSignal signal,
    IOptions<OpenIddictSettings> openIddict,
    TimeProvider clock,
    ILogger<PendingDeletionProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Processing pending account data deletions failed; it will be retried");
            }

            try
            {
                await signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over the pending deletions. Public so tests can run it directly.</summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var horizon = openIddict.Value.AccessTokenLifetime + TimeSpan.FromMinutes(1);
        var pending = await db.PendingDataDeletions.OrderBy(d => d.RequestedAt).Take(50).ToListAsync(cancellationToken);

        foreach (var deletion in pending)
        {
            var now = clock.GetUtcNow();
            deletion.Attempts++;
            deletion.LastAttemptAt = now;
            try
            {
                await api.DeleteUserDataAsync(deletion.UserId, cancellationToken);
                deletion.LastError = null;
                if (now - deletion.RequestedAt >= horizon)
                {
                    db.PendingDataDeletions.Remove(deletion); // done: no valid access token can exist any more
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                deletion.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogActivity(LogCategories.Security, "account.data_delete", EventOutcome.Failure, new Dictionary<string, object?>
                {
                    ["Attempts"] = deletion.Attempts,
                    ["Reason"] = ex.GetType().Name,
                }, deletion.UserId, LogLevel.Error, ex);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
