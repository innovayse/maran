using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Notifications.Jobs;

/// <summary>
/// Deletes notifications older than <see cref="RetentionWindow"/> and any beyond the
/// <see cref="MaxKept"/> newest, in bounded batches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two bounds, because either alone leaves the table unbounded in one direction.</b> Age alone
/// lets a burst of failures (a hundred sites failing at once) fill the feed for ninety days; count
/// alone keeps a quiet server's ancient entries for ever. Both are applied each pass, age first.
/// Dismissals leave with their notification through the foreign key's cascade, so no per-administrator
/// residue outlives the entry it was about.
/// </para>
/// <para>
/// <b>Batches are loaded and removed through the context</b> (at most <see cref="BatchSize"/> rows at
/// a time) rather than by a set-based delete: the batch is small by construction, the same code runs
/// under the in-memory test provider, and a cancelled pass has committed the batches it finished.
/// The module's context has no tenant filter, so no filter needs bypassing here.
/// </para>
/// <para>Housekeeping, so it is logged and not journalled, like the task retention it mirrors.</para>
/// </remarks>
public sealed class NotificationRetentionHandler
{
    /// <summary>How long an entry is kept.</summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(90);

    /// <summary>The most entries kept however recent.</summary>
    public const int MaxKept = 500;

    /// <summary>How many rows one delete removes.</summary>
    private const int BatchSize = 200;

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Exception?> LogPurged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(NotificationRetentionHandler)),
            "Purged {Purged} panel notifications past the retention window or beyond the kept count");

    /// <summary>The Notifications module's database context.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<NotificationRetentionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Notifications module's database context.</param>
    /// <param name="clock">The time source the window is measured against.</param>
    /// <param name="logger">Where the outcome is reported.</param>
    public NotificationRetentionHandler(
        NotificationsDbContext dbContext,
        IClock clock,
        ILogger<NotificationRetentionHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one retention pass.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between batches.</param>
    /// <returns>How many rows were deleted.</returns>
    public async Task<int> HandleAsync(NotificationRetentionRequested message, CancellationToken cancellationToken)
    {
        var cutoff = _clock.UtcNow - RetentionWindow;
        var purged = 0;

        while (true)
        {
            var batch = await _dbContext.PanelNotifications
                .Where(notification => notification.OccurredAt < cutoff)
                .OrderBy(notification => notification.OccurredAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (!await RemoveAsync(batch, cancellationToken))
            {
                break;
            }

            purged += batch.Count;
        }

        while (true)
        {
            var batch = await _dbContext.PanelNotifications
                .OrderByDescending(notification => notification.OccurredAt)
                .ThenByDescending(notification => notification.Id)
                .Skip(MaxKept)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (!await RemoveAsync(batch, cancellationToken))
            {
                break;
            }

            purged += batch.Count;
        }

        LogPurged(_logger, purged, null);

        return purged;
    }

    /// <summary>Removes one batch.</summary>
    /// <param name="batch">The rows to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>false</c> when the batch was empty, which ends the loop that read it.</returns>
    private async Task<bool> RemoveAsync(List<PanelNotification> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return false;
        }

        _dbContext.PanelNotifications.RemoveRange(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
