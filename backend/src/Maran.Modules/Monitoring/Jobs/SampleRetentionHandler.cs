using Maran.Modules.Monitoring.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Monitoring.Jobs;

/// <summary>
/// Deletes raw metric samples older than <see cref="RetentionWindow"/>, in bounded batches. The one
/// thing that keeps <c>monitoring.Samples</c> from growing for as long as the panel runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seven days of RAW samples, and no rollup table (R10).</b> At one sample a minute that is about
/// 10,080 rows — a table PostgreSQL buckets on read, with <c>date_bin</c>, faster than the browser
/// can draw the result. The alternative shape, a rollup table written beside every sample, would buy
/// nothing at this size and cost a second write path per minute plus the standing possibility that
/// the summary and the samples disagree about the same hour.
/// </para>
/// <para>
/// <b>Seven days, not the Tasks module's thirty.</b> The window is short precisely because the rows
/// are dense: a task row is written per administrator operation, this one per minute. Seven days is
/// also exactly what the charts can ask for, so the retention window and the longest range the
/// interface offers are the same number by construction — a chart that could ask for more would draw
/// a line that stops partway across with nothing saying why.
/// </para>
/// <para>
/// <b>The delete is batched.</b> A server that ran for a year with this handler unshipped, then
/// upgraded, can find half a million eligible rows on its first pass; one <c>DELETE</c> covering all
/// of them holds its locks and its WAL growth for as long as that single statement runs, on the
/// table the sampler is writing to every minute. Each iteration instead removes at most
/// <see cref="BatchSize"/> rows, so the worst case is many short statements, and a pass cancelled by
/// a shutdown has already committed the batches it finished.
/// </para>
/// <para>
/// <b>Nothing here is journalled.</b> Housekeeping is not an operation an operator watches and not a
/// security-relevant decision; how many rows were purged goes to the log, which is where an operator
/// already looks to confirm a background pass ran at all.
/// </para>
/// </remarks>
public sealed class SampleRetentionHandler
{
    /// <summary>How long a raw sample is kept before it becomes eligible for deletion.</summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);

    /// <summary>How many rows one delete statement removes, bounding each statement's lock and WAL cost.</summary>
    private const int BatchSize = 1000;

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Exception?> LogPurged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(SampleRetentionHandler)),
            "Purged {Purged} monitoring samples older than the retention window");

    /// <summary>The Monitoring module's database context.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<SampleRetentionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Monitoring module's database context.</param>
    /// <param name="clock">The injected time source the retention window is measured against.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public SampleRetentionHandler(
        MonitoringDbContext dbContext,
        IClock clock,
        ILogger<SampleRetentionHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one retention pass, deleting every sample older than the window.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between batches.</param>
    /// <returns>How many rows were deleted; zero is the ordinary outcome once the table is caught up.</returns>
    public async Task<int> HandleAsync(SampleRetentionRequested message, CancellationToken cancellationToken)
    {
        var cutoff = _clock.UtcNow - RetentionWindow;
        var purged = 0;

        while (true)
        {
            var batch = await SelectBatchAsync(cutoff, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            await _dbContext.Samples
                .Where(sample => batch.Contains(sample.Id))
                .ExecuteDeleteAsync(cancellationToken);

            purged += batch.Count;
        }

        LogPurged(_logger, purged, null);

        return purged;
    }

    /// <summary>Reads the ids of the next batch of eligible rows, oldest first.</summary>
    /// <param name="cutoff">Samples taken before this instant are eligible.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Up to <see cref="BatchSize"/> ids; empty once nothing more qualifies.</returns>
    /// <remarks>
    /// Oldest first so that a pass interrupted partway through has already removed the
    /// longest-overdue rows rather than an arbitrary subset, and so the next pass resumes exactly
    /// where progress would have continued anyway.
    /// </remarks>
    private Task<List<long>> SelectBatchAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return _dbContext.Samples
            .Where(sample => sample.CapturedAt < cutoff)
            .OrderBy(sample => sample.CapturedAt)
            .Select(sample => sample.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }
}
