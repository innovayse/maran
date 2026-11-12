using System.Data.Common;
using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Settles a ledger row left <see cref="AppState.Installing"/> or <see cref="AppState.Removing"/> whose
/// operation is over, against what the host reports, while the panel runs (issue #533).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside the start-up pass.</b> The start-up pass settles every row once, at boot. A row
/// whose operation ended without the row being told (the runner's own save failed after the task closed, a
/// task pruned from under it) stays busy for as long as the panel runs, and the screen hides that app's
/// actions meanwhile. This asks, whenever the apps are read, whether the task of each busy row can still be
/// running, and judges the rows that cannot.
/// </para>
/// <para>
/// <b>A running task is left alone, and so is a row with no task id.</b> The task is the only evidence that
/// the operation is in flight: a row whose task is <see cref="TaskState.Running"/> is judged by nobody
/// else's clock. A row holding the empty id has no task to ask about (the recorder could not record one
/// while the operation ran on), so a running operation cannot be told from a dead one and the row is left
/// for the start-up pass, which does not need to tell.
/// </para>
/// <para>
/// <b>The host decides, through the same rule as at start-up</b> (<see cref="AppLedgerSettler"/>). The
/// operation's own runner settles the ledger BEFORE it closes the task, so a task that is over with a row
/// still busy is exactly the case this repairs; a runner that is mid-way has a task that is running.
/// </para>
/// <para>
/// <b>Only the operation that was asked about is judged.</b> The rows are read again when the verdicts are
/// made, after the task was asked about, and a row that is no longer installing or removing on the same task
/// (its runner settled it, or a retry gave it a new task) is a different operation and is left alone; the
/// concurrency token on the row refuses the save of a verdict made on a row that changed after that re-read.
/// </para>
/// <para>
/// <b>It never fails a read.</b> A save that lost a race to another writer is skipped (the next read judges
/// the row afresh); any other database fault is logged and skipped, because the list matters more than the
/// settling; an unreachable agent never reaches here, because the caller already holds the host's list.
/// </para>
/// </remarks>
public sealed class StaleOperationSettler
{
    /// <summary>Pre-compiled log delegate for rows settled at runtime.</summary>
    private static readonly Action<ILogger, int, int, int, Exception?> LogSettled =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(StaleOperationSettler)),
            "Settled stale app rows against the host: {Installed} marked installed, {Failed} marked failed, {Removed} removed");

    /// <summary>Pre-compiled log delegate for a save that lost a race.</summary>
    private static readonly Action<ILogger, Exception?> LogConflictSkipped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(StaleOperationSettler)),
            "The app ledger changed while stale rows were being settled; they are judged again on the next read");

    /// <summary>Pre-compiled log delegate for a save that failed for a reason other than a race.</summary>
    private static readonly Action<ILogger, Exception?> LogSettleFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(StaleOperationSettler)),
            "Stale app rows could not be settled; the apps are listed as they are and judged again on the next read");

    /// <summary>The ledger.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>Tells whether a task can still be running.</summary>
    private readonly ITaskStateReader _tasks;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome is reported.</summary>
    private readonly ILogger<StaleOperationSettler> _logger;

    /// <summary>Creates the settler.</summary>
    /// <param name="dbContext">The ledger.</param>
    /// <param name="tasks">Tells whether a task can still be running.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="logger">Where the outcome is reported.</param>
    public StaleOperationSettler(
        AppsDbContext dbContext,
        ITaskStateReader tasks,
        IClock clock,
        ILogger<StaleOperationSettler> logger)
    {
        _dbContext = dbContext;
        _tasks = tasks;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Settles the busy rows whose operation is over.</summary>
    /// <param name="hostApps">What the host reports, already read by the caller.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes once the rows are settled.</returns>
    public async Task SettleAsync(IReadOnlyList<AgentApp> hostApps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostApps);

        var busy = await _dbContext.InstalledApps
            .AsNoTracking()
            .Where(row => row.State == AppState.Installing || row.State == AppState.Removing)
            .Select(row => new { row.Id, row.TaskId })
            .ToListAsync(cancellationToken);

        var stale = new Dictionary<Guid, Guid>();
        foreach (var row in busy)
        {
            if (row.TaskId is not { } taskId || taskId == Guid.Empty)
            {
                continue;
            }

            if (await _tasks.ReadAsync(taskId, cancellationToken) != TaskState.Running)
            {
                stale[row.Id] = taskId;
            }
        }

        if (stale.Count == 0)
        {
            return;
        }

        var settler = new AppLedgerSettler(_dbContext, _clock, _logger);
        try
        {
            var settlement = await settler.SettleRowsAsync(AppLedgerSettler.HostAppsById(hostApps), stale, cancellationToken);
            LogSettled(_logger, settlement.Installed, settlement.Failed, settlement.Removed, null);
        }
        catch (DbUpdateException exception) when (AppLedgerConflicts.Classify(exception) is not null)
        {
            _dbContext.ChangeTracker.Clear();
            LogConflictSkipped(_logger, null);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            _dbContext.ChangeTracker.Clear();
            LogSettleFailed(_logger, exception);
        }
    }
}
