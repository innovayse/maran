using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Closes every task that was still running when the panel last stopped, once the panel has started.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this the panel tells a visible lie.</b> A task is opened by one process and closed by
/// the same one; a process that is stopped, restarted or killed in between leaves its row
/// <see cref="PanelTaskStatus.Running"/> for ever. Nothing will ever close it — the operation it
/// described died with the process — so the listing shows work in flight that is not, and a watcher's
/// stream never reaches its ending because the ending never comes. An operator staring at a
/// half-finished account deletion has no way to learn that it stopped hours ago, which is worse than
/// having no progress bar at all.
/// </para>
/// <para>
/// <b>The cut-off is this process's own start, and that is what makes the pass safe to run late.</b>
/// A hosted service starts alongside the web server, not strictly before it, so a task opened by the
/// first request to arrive can be in the table while this pass is still reading it. Closing every
/// running row would then kill a live operation's record on every boot — the failure would look
/// exactly like the one this class exists to fix, and would be caused by the fix. So the instant the
/// reconciler was constructed is captured once, and only tasks that started BEFORE it are closed:
/// those, and only those, belong to a process that is gone.
/// </para>
/// <para>
/// <b>It reads with <c>IgnoreQueryFilters</c>, and must.</b> The module's query filter admits
/// administrators, and a hosted service has no signed-in caller at all — so the filtered read would
/// find nothing, every time, and the class would appear to work while doing nothing. It is safe
/// because nothing read here is returned to anybody: the rows are closed in place.
/// </para>
/// <para>
/// <b><see cref="PanelTask.FinishedAt"/> becomes the instant the panel NOTICED</b>, not the
/// instant the task died, because nothing recorded the latter — the process that would have written
/// it is what stopped. The error code says what happened, and the sentence behind it in every
/// language is the one an operator reads.
/// </para>
/// <para>
/// A failed pass is retried a bounded number of times: at boot the database may not be reachable
/// yet, and a pass that has failed <see cref="MaximumAttempts"/> times is a broken host rather than
/// a slow start.
/// </para>
/// </remarks>
public sealed class StartupTaskReconciler : BackgroundService
{
    /// <summary>How many times a failed pass is retried before the reconciler gives up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Exception?> LogReconciled =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(StartupTaskReconciler)),
            "Closed {Closed} panel tasks that were still running when the panel last stopped");

    /// <summary>Pre-compiled log delegate for a pass that did not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(2, nameof(StartupTaskReconciler)),
            "Could not close abandoned panel tasks on attempt {Attempt}; the panel may be showing "
            + "operations as running that stopped with the last process");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(3, nameof(StartupTaskReconciler)),
            "Gave up closing abandoned panel tasks after {Attempts} attempts; any task left running "
            + "by the previous process will show as running until the panel is restarted");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <see cref="TasksDbContext"/>. A <see cref="BackgroundService"/> is a
    /// singleton, the context is scoped, and a singleton capturing a scoped dependency is refused by
    /// the container at BUILD time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<StartupTaskReconciler> _logger;

    /// <summary>
    /// When this process started, as the only boundary between a task that was abandoned and a task
    /// that is running right now. Read once, in the constructor, because the container builds hosted
    /// services before the first request is served — reading it per pass would move the boundary
    /// forward across a retry and let the second attempt close what the first correctly spared.
    /// </summary>
    private readonly DateTimeOffset _processStartedAt;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="clock">The panel's clock, which fixes the boundary and stamps the outcomes.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public StartupTaskReconciler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<StartupTaskReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
        _processStartedAt = clock.UtcNow;
    }

    /// <summary>Closes every task the previous process left running, once.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many tasks were closed; zero is the ordinary outcome of a clean restart.</returns>
    /// <remarks>
    /// Public because it is the pass, and the pass is what has behaviour worth asserting: a test
    /// drives it directly rather than starting a hosted service and waiting for a timer, which is
    /// the sleep rules/testing.md forbids.
    /// </remarks>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        // Both halves of the predicate carry weight. The status is what makes a row a candidate; the
        // boundary is what makes the pass safe to run late, because a task this process opened is
        // running for a reason and closing it would be the very failure this class exists to fix.
        var boundary = _processStartedAt;
#pragma warning disable RS0030 // reconciliation runs at startup, before any request, and must see tasks left by the dead process
        var abandoned = await dbContext.PanelTasks
            .IgnoreQueryFilters()
            .Where(task => task.Status == PanelTaskStatus.Running && task.StartedAt < boundary)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        if (abandoned.Count == 0)
        {
            LogReconciled(_logger, 0, null);
            return 0;
        }

        var at = _clock.UtcNow;
        foreach (var task in abandoned)
        {
            task.Fail(nameof(ErrorMessages.TaskAbandonedByRestart), at);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        LogReconciled(_logger, abandoned.Count, null);

        return abandoned.Count;
    }

    /// <summary>Runs passes until one succeeds or the attempts run out.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves when the pass has succeeded or the attempts are spent.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (await AttemptAsync(attempt, stoppingToken))
                {
                    return;
                }

                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }

            LogGaveUp(_logger, MaximumAttempts, null);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one: a hosted service that
            // reported an error on every clean stop trains an operator to ignore its errors.
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a failed attempt.</summary>
    /// <param name="attempt">Which attempt this is, so the log line names it.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>True when the pass completed.</returns>
    /// <remarks>
    /// A database that is not reachable yet arrives here as an exception rather than as a failed
    /// <c>Result</c>. Letting it escape <see cref="ExecuteAsync"/> would stop the service for the
    /// lifetime of the process — the same outcome as never having written it, on the one boot where
    /// it mattered most.
    /// </remarks>
    private async Task<bool> AttemptAsync(int attempt, CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAttemptFailed(_logger, attempt, exception);
            return false;
        }
    }
}
