using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Tasks.Jobs;

/// <summary>
/// Deletes finished panel tasks older than <see cref="RetentionWindow"/>, in bounded batches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this <c>tasks.PanelTasks</c> grows forever.</b> Every panel operation instrumented
/// with <c>ITaskRecorder</c> writes a row that, before this handler, nothing ever removed — a
/// long-lived server accumulates one row per certificate order, renewal and account deletion for as
/// long as it runs. The table is small per row (bounded by <c>PanelTask.MaxLogLength</c>) but
/// unbounded in row count, which is exactly the shape a retention job exists to fix.
/// </para>
/// <para>
/// <b>Thirty days, not R10's seven.</b> Monitoring's seven-day window is sized for RAW SAMPLES taken
/// every sixty seconds — the volume is the whole reason that window is short. A task row is written
/// per ADMINISTRATOR OPERATION, orders of magnitude rarer, and its purpose is different too: an
/// operator reading the feed asks "what did the panel do, and why did something fail", which is a
/// question worth being able to answer for the operator's last few work cycles, not just the last
/// week. Thirty days also lines up with the panel's own longest existing cadence —
/// <c>CertificateRenewalHandler.RenewalWindow</c> — so a renewal pass that started struggling with a
/// domain a few weeks ago still has its whole history in view. It is deliberately NOT the audit
/// log's window: <c>TaskKinds</c>' own remarks are explicit that a task is "operational state" and
/// the journal is "the permanent security record" — folding them together would make every progress
/// bar part of a security record it was never meant to join, which argues for weeks bounded by an
/// operator's own attention span rather than months or forever.
/// </para>
/// <para>
/// <b>Completed and failed rows are purged identically, on purpose.</b> There is one predicate and
/// one window for both outcomes rather than a longer allowance for failures. A failure is not
/// entitled to less scrutiny than a success — the feed exists in part so an operator can see why
/// something failed, and a success is just as often what an operator needs to confirm actually
/// happened — so treating them alike is the simpler rule and the one with no argument for the
/// asymmetry the other way. Nothing here special-cases <see cref="Domain.Enums.PanelTaskStatus"/> at
/// all: both final states set <see cref="PanelTask.FinishedAt"/> the same way, and it is the
/// one column this handler reads.
/// </para>
/// <para>
/// <b>A RUNNING task can never be selected, structurally rather than by a status check.</b> The
/// predicate is <c>FinishedAt != null &amp;&amp; FinishedAt &lt; cutoff</c>. <see cref="PanelTask"/>
/// sets <c>FinishedAt</c> in exactly two places — <see cref="PanelTask.Complete"/> and
/// <see cref="PanelTask.Fail"/> — and nowhere else, so a row with a null <c>FinishedAt</c> IS
/// a running row by the entity's own construction, not by a status column this handler would have to
/// trust separately. A task abandoned by a crashed process stays running (and therefore un-purged)
/// until <c>StartupTaskReconciler</c> closes it on the next boot; only after that does it start
/// aging toward this window, which is the correct order — a row nobody has explained yet is a row
/// this handler must not make disappear.
/// </para>
/// <para>
/// <b>The delete is batched, and that is what "bounded" means here.</b> A server that goes a year
/// with this handler unshipped, then upgrades, can find hundreds of thousands of eligible rows on
/// its very first pass. One <c>DELETE</c> covering all of them would hold its row locks and its
/// WAL growth for as long as that single statement runs, on a table every other request also reads.
/// Instead each iteration reads at most <see cref="BatchSize"/> ids (indexed by
/// <c>IX_PanelTasks_FinishedAt</c>, oldest first) and deletes exactly that batch before starting the
/// next — so the worst case is many short statements instead of one long one, and a cancelled pass
/// (host shutting down) has already committed whatever batches it finished rather than losing all
/// its progress to a rolled-back giant transaction.
/// </para>
/// <para>
/// <b>Nothing here reads or writes through <see cref="ITaskRecorder"/>, and does not journal to
/// the audit log either.</b> A retention sweep is housekeeping, not an operation an operator watches
/// or a security-relevant decision — it is the same category <c>StartupBanReconciler</c> places
/// itself in when it says "nothing is journalled here" about re-applying a ban that was already the
/// journalled decision. How many rows were purged is reported to the log, which is where an operator
/// already looks to confirm a background pass ran at all.
/// </para>
/// <para>
/// <b>It reads and deletes with <c>IgnoreQueryFilters</c>, and must.</b> This runs unattended, with
/// no signed-in caller, exactly like <c>CertificateRenewalHandler.SelectDueAsync</c> — the module's
/// query filter admits only <c>ICurrentUser.IsAdmin</c>, which nobody satisfies here. Without
/// <c>IgnoreQueryFilters</c> both the read and the delete would silently match nothing, every night,
/// forever — the exact failure mode a filtered write would produce, stated once here as the reason
/// it is never applied to either.
/// </para>
/// </remarks>
public sealed class TaskRetentionHandler
{
    /// <summary>How long a finished task is kept before it becomes eligible for deletion.</summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);

    /// <summary>How many rows one delete statement removes, bounding each statement's lock and WAL cost.</summary>
    private const int BatchSize = 500;

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Exception?> LogPurged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(TaskRetentionHandler)),
            "Purged {Purged} panel tasks that finished more than the retention window ago");

    /// <summary>The Tasks module's database context.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<TaskRetentionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="clock">The injected time source the retention window is measured against.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public TaskRetentionHandler(TasksDbContext dbContext, IClock clock, ILogger<TaskRetentionHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one retention pass, deleting every finished task older than the window.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between batches.</param>
    /// <returns>How many rows were deleted; zero is the ordinary outcome once the table is caught up.</returns>
    public async Task<int> HandleAsync(TaskRetentionRequested message, CancellationToken cancellationToken)
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

#pragma warning disable RS0030 // unattended retention runs with no principal; scoped, it would delete nothing, every night
            await _dbContext.PanelTasks
                .IgnoreQueryFilters()
                .Where(task => batch.Contains(task.Id))
                .ExecuteDeleteAsync(cancellationToken);
#pragma warning restore RS0030

            purged += batch.Count;
        }

        LogPurged(_logger, purged, null);

        return purged;
    }

    /// <summary>Reads the ids of the next batch of eligible rows, oldest finish first.</summary>
    /// <param name="cutoff">Rows that finished before this instant are eligible.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Up to <see cref="BatchSize"/> ids; empty once nothing more qualifies.</returns>
    /// <remarks>
    /// Oldest first so that a pass interrupted partway through — cancellation, a restart between
    /// batches — has already removed the longest-overdue rows rather than an arbitrary subset, and
    /// so the next pass resumes exactly where progress would have continued anyway.
    /// </remarks>
    private Task<List<Guid>> SelectBatchAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended retention runs with no principal; scoped, it would delete nothing, every night
        return _dbContext.PanelTasks
            .IgnoreQueryFilters()
            .Where(task => task.FinishedAt != null && task.FinishedAt < cutoff)
            .OrderBy(task => task.FinishedAt)
            .Select(task => task.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }
}
