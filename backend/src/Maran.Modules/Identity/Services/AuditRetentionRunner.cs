using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Deletes the audit journal's old end, and nothing else: only a SEALED PREFIX of the hash chain, only
/// rows older than the retention window, and only after the chain anchor has been advanced over them
/// in the same atomic write.
/// </summary>
/// <remarks>
/// <para>
/// <b>It fails closed.</b> Every doubt is a run that deletes nothing: no policy row, a window of
/// <c>null</c>, a window outside the entity's bounds, a batch whose chain indexes do not continue
/// from the anchor without a gap, or a sealed row with no hash to anchor on. "Cannot tell" and "keep"
/// are the same answer (threat note, section 8).
/// </para>
/// <para>
/// <b>The prefix rule.</b> The cutoff is turned into a chain index first: the purge runs through one
/// less than the smallest index among SEALED rows still inside the window. A row that is older than the
/// cutoff but committed late, and so carries a higher index than a row inside the window, is KEPT; the
/// rule errs towards keeping, because purging it would leave a hole in the middle of the chain and not
/// a shorter end. When no sealed row is inside the window, every sealed row is older than the cutoff and
/// the purge runs through the highest sealed index. An unsealed row has no index, so it can never be
/// selected: the maintenance run seals before it purges, and a row that missed the seal waits for the
/// next one.
/// </para>
/// <para>
/// <b>The anchor and the deletes are ONE <c>SaveChanges</c>, per batch of
/// <see cref="PurgeBatchSize"/>.</b> EF Core commits one save as one database transaction, so the
/// anchor moves if and only if its batch is gone: a crash leaves the old state or the new one, never
/// rows deleted with the anchor behind (which verification would read as a break) or an anchor ahead
/// of rows still present. The anchor takes its hash from the last row of the batch, read from the
/// batch itself before it is removed. The write is deliberately not an <c>ExecuteDelete</c>: a bulk
/// delete would be a second statement outside that save, and the two could part.
/// </para>
/// <para>
/// <b>No <c>IgnoreQueryFilters</c> is needed</b> and none is used: neither the journal nor the anchor
/// is a tenant table, so the run sees every row with no principal at all.
/// </para>
/// <para>
/// <b>Not here:</b> journalling. The run reports what it did; the maintenance handler writes the
/// <c>AuditRetentionRun</c> entry, so a run that threw can be journalled as failed by the one place
/// that sees the exception.
/// </para>
/// </remarks>
public sealed class AuditRetentionRunner
{
    /// <summary>How many rows one save removes, bounding each transaction's lock and write-ahead-log cost.</summary>
    public const int PurgeBatchSize = 1000;

    /// <summary>Pre-compiled log delegate for a run that removed rows.</summary>
    private static readonly Action<ILogger, int, long, Exception?> LogPurged =
        LoggerMessage.Define<int, long>(
            LogLevel.Information,
            new EventId(1, nameof(AuditRetentionRunner)),
            "Audit retention purged {Purged} journal rows through chain index {ThroughChainIndex}");

    /// <summary>Pre-compiled log delegate for a run that stopped because the chain did not line up.</summary>
    private static readonly Action<ILogger, long, Exception?> LogStoppedOnDoubt =
        LoggerMessage.Define<long>(
            LogLevel.Warning,
            new EventId(2, nameof(AuditRetentionRunner)),
            "Audit retention stopped without purging further: the sealed rows after chain index {AnchoredThrough} do not continue the chain");

    /// <summary>Pre-compiled log delegate for a stored window the entity would refuse.</summary>
    private static readonly Action<ILogger, Exception?> LogWindowNotTrusted =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(AuditRetentionRunner)),
            "Audit retention did not run: the stored window is outside the accepted bounds");

    /// <summary>The module's database context, which owns the journal, the policy and the anchor.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each run is reported.</summary>
    private readonly ILogger<AuditRetentionRunner> _logger;

    /// <summary>Creates the runner.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The time source the window is measured against.</param>
    /// <param name="logger">Where the outcome of each run is reported.</param>
    public AuditRetentionRunner(IdentityDbContext dbContext, IClock clock, ILogger<AuditRetentionRunner> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one retention pass over the audit journal.</summary>
    /// <param name="cancellationToken">Cancels the run between batches; what earlier batches removed stays removed.</param>
    /// <returns>What the run did; <see cref="AuditRetentionOutcome.Purged"/> is zero when nothing was eligible.</returns>
    public async Task<AuditRetentionOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var stored = await _dbContext.AuditChainAnchors
            .FirstOrDefaultAsync(row => row.Id == AuditChainAnchor.SingletonId, cancellationToken);

        var anchorIsNew = stored is null;
        var anchor = stored ?? AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, now);
        var anchoredThrough = anchor.PurgedThroughIndex;

        var days = await _dbContext.AuditRetentionPolicies
            .AsNoTracking()
            .Where(row => row.Id == AuditRetentionPolicy.SingletonId)
            .Select(row => row.RetentionDays)
            .FirstOrDefaultAsync(cancellationToken);

        if (days is null)
        {
            return new AuditRetentionOutcome(0, anchoredThrough, null);
        }

        if (!AuditRetentionPolicy.IsPermitted(days))
        {
            LogWindowNotTrusted(_logger, null);

            return new AuditRetentionOutcome(0, anchoredThrough, null);
        }

        var cutoff = now - TimeSpan.FromDays(days.Value);
        var purgeThrough = await FindPurgeThroughAsync(cutoff, cancellationToken);

        if (purgeThrough is null || purgeThrough.Value <= anchoredThrough)
        {
            return new AuditRetentionOutcome(0, anchoredThrough, cutoff);
        }

        var purged = await PurgeAsync(anchor, anchorIsNew, purgeThrough.Value, now, cancellationToken);

        return new AuditRetentionOutcome(purged, anchor.PurgedThroughIndex, cutoff);
    }

    /// <summary>Turns the cutoff into the highest chain index the run may purge.</summary>
    /// <param name="cutoff">Rows older than this are eligible.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// One less than the smallest sealed index inside the window, or the highest sealed index when none
    /// is inside it; <c>null</c> when no row is sealed at all.
    /// </returns>
    private async Task<long?> FindPurgeThroughAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var firstKept = await _dbContext.AuditEvents
            .Where(row => row.ChainIndex != null && row.OccurredAt >= cutoff)
            .MinAsync(row => row.ChainIndex, cancellationToken);

        if (firstKept is not null)
        {
            return firstKept.Value - 1;
        }

        return await _dbContext.AuditEvents
            .Where(row => row.ChainIndex != null)
            .MaxAsync(row => row.ChainIndex, cancellationToken);
    }

    /// <summary>Removes the sealed prefix in batches, advancing the anchor with each.</summary>
    /// <param name="anchor">The anchor: the tracked row, or a genesis anchor not yet stored.</param>
    /// <param name="anchorIsNew">Whether <paramref name="anchor"/> still has to be added to the context.</param>
    /// <param name="purgeThrough">The highest chain index that may be removed.</param>
    /// <param name="now">When the run happens, stamped on the anchor.</param>
    /// <param name="cancellationToken">Cancels the run between batches.</param>
    /// <returns>How many rows were removed.</returns>
    private async Task<int> PurgeAsync(
        AuditChainAnchor anchor,
        bool anchorIsNew,
        long purgeThrough,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var purged = 0;

        while (true)
        {
            var batch = await _dbContext.AuditEvents
                .Where(row => row.ChainIndex != null && row.ChainIndex <= purgeThrough)
                .OrderBy(row => row.ChainIndex)
                .Take(PurgeBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            if (!ContinuesFrom(batch, anchor.PurgedThroughIndex))
            {
                LogStoppedOnDoubt(_logger, anchor.PurgedThroughIndex, null);
                break;
            }

            var last = batch[^1];
            anchor.Advance(last.ChainIndex!.Value, last.RowHash!, now);

            if (anchorIsNew)
            {
                _dbContext.AuditChainAnchors.Add(anchor);
                anchorIsNew = false;
            }

            _dbContext.AuditEvents.RemoveRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            purged += batch.Count;
        }

        if (purged > 0)
        {
            LogPurged(_logger, purged, anchor.PurgedThroughIndex, null);
        }

        return purged;
    }

    /// <summary>Says whether a batch is the next unbroken stretch of the chain after the anchor.</summary>
    /// <param name="batch">Sealed rows in ascending chain-index order.</param>
    /// <param name="anchoredThrough">The highest index already purged.</param>
    /// <returns>
    /// <c>true</c> when the indexes run one by one from the anchor onwards and the last row carries a
    /// hash to anchor on; anything else is a doubt, and a doubt purges nothing.
    /// </returns>
    private static bool ContinuesFrom(List<AuditEvent> batch, long anchoredThrough)
    {
        var first = batch[0].ChainIndex;
        var last = batch[^1];

        return first == anchoredThrough + 1
            && last.ChainIndex == anchoredThrough + batch.Count
            && last.RowHash is not null;
    }
}
