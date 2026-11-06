using Maran.Modules.Idempotency.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Idempotency.Jobs;

/// <summary>
/// Deletes idempotency records past their expiry, in bounded batches: reservations whose lease ran out
/// and completed responses older than the replay window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this the table grows for ever, and so does the amount of customer data at rest in
/// clear.</b> A stored response is a copy of what a request returned; the 24-hour window is the promise
/// about how long that copy lives, and this pass is what keeps it.
/// </para>
/// <para>
/// <b>It needs no filter bypass.</b> The module's context has no tenant query filter (records are scoped
/// by caller key, not by account), so the unattended pass, which has no principal, reads exactly what is
/// there. Both the lease and the window are encoded in <c>ExpiresAt</c> by the entity, so one predicate
/// covers every state.
/// </para>
/// <para>
/// <b>The delete is batched</b> for the reason <c>TaskRetentionHandler</c> batches: a pass after a long
/// gap would otherwise hold one statement's locks over a table every idempotent request reads. Oldest
/// expiry first, so an interrupted pass has removed the longest-overdue rows.
/// </para>
/// <para>
/// Nothing here is journalled: housekeeping is not a decision. The count of removed rows goes to the
/// log.
/// </para>
/// </remarks>
public sealed class ExpiredRecordPurgeHandler
{
    /// <summary>How many rows one batch removes, bounding each save's lock and WAL cost.</summary>
    private const int BatchSize = 500;

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Exception?> LogPurged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(ExpiredRecordPurgeHandler)),
            "Purged {Purged} expired idempotency records");

    /// <summary>The module's database context.</summary>
    private readonly IdempotencyDbContext _dbContext;

    /// <summary>The injected time source expiry is measured against.</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<ExpiredRecordPurgeHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The injected time source expiry is measured against.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public ExpiredRecordPurgeHandler(
        IdempotencyDbContext dbContext, IClock clock, ILogger<ExpiredRecordPurgeHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one purge, deleting every record whose expiry has passed.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between batches.</param>
    /// <returns>How many rows were deleted; zero is the ordinary outcome once the table is caught up.</returns>
    public async Task<int> HandleAsync(ExpiredRecordPurgeRequested message, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var purged = 0;

        while (true)
        {
            var batch = await _dbContext.IdempotencyRecords
                .Where(record => record.ExpiresAt <= now)
                .OrderBy(record => record.ExpiresAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            _dbContext.IdempotencyRecords.RemoveRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            purged += batch.Count;
        }

        LogPurged(_logger, purged, null);

        return purged;
    }
}
