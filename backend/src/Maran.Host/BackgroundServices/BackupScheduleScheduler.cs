using Maran.Modules.Backups.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="BackupRunRequested"/> every five minutes, which is the only thing that makes
/// the panel's backup schedules actually run.
/// </summary>
/// <remarks>
/// Without it the Backups module ships with a fully implemented, fully tested schedule sweep that
/// never runs, and <c>BackupKind.Scheduled</c> is an enum value with no writer — which is precisely
/// the shape of gap this scheduler closes, mirroring <see cref="TaskRetentionScheduler"/> and
/// <see cref="MetricsRetentionScheduler"/> exactly.
///
/// It is a hosted service inside <c>maran-api</c>, not a new process, and not the Cron module: the
/// system is fixed at three processes (rules/architecture.md) and rules/security.md item 10 forbids
/// a new daemon without a spec change, while a cron entry lives in a file the customer's own account
/// can edit and runs as that account's UID — which is not what a root operation reading every file
/// in a home and dumping its databases can be built on. The work itself is a Wolverine message, so a
/// sweep that fails is visible and retriable where every other failed message is.
///
/// <b>Five minutes, not a day, and that is the one place this differs from the two schedulers it
/// otherwise copies.</b> A schedule names an hour of the day, so the cadence has to be fine enough
/// that the hour is noticed within itself; five minutes is twelve chances inside the named hour and
/// costs, on a tick with nothing due, one indexed read of a table with one row per configured
/// schedule. Making the cadence coarser would mean a schedule set for 03:00 running at some other
/// time; making it finer would buy nothing, because a backup that starts at 03:04 instead of 03:01
/// is the same backup.
///
/// <b>The first sweep runs two minutes after startup, BEFORE the three daily passes' five, ten and
/// fifteen.</b> The offsets exist so that a fresh boot does not start every daily pass at once and
/// contend for the same database connections; this one is put first rather than last because it is
/// the cheap one — it usually publishes a message that selects nothing — and because it will run
/// again in five minutes anyway, so nothing is gained by making it wait.
/// </remarks>
public sealed class BackupScheduleScheduler : BackgroundService
{
    /// <summary>How long between sweeps over the panel's backup schedules.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>How long after startup the first sweep runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>Pre-compiled log delegate for a sweep this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(BackupScheduleScheduler)),
            "Could not queue the backup schedule sweep; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>IMessageBus</c>, for the reason
    /// <see cref="CertificateRenewalScheduler"/> resolves one the same way: a
    /// <see cref="BackgroundService"/> is a singleton, Wolverine registers <c>IMessageBus</c> as
    /// scoped, and capturing it directly is refused by the container at BUILD time rather than
    /// degrading gracefully at runtime.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the sweep is reported.</summary>
    private readonly ILogger<BackupScheduleScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the sweep is reported.</param>
    public BackupScheduleScheduler(IServiceScopeFactory scopeFactory, ILogger<BackupScheduleScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Publishes the trigger on the interval until the host shuts down.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                await PublishAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one: a hosted service that
            // reported an error on every clean stop trains an operator to ignore its errors.
        }
    }

    /// <summary>Publishes one trigger, turning a bus failure into a log line rather than a crash.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <remarks>
    /// An unhandled exception out of <see cref="ExecuteAsync"/> stops the service for the lifetime of
    /// the process, so one bad tick would silently end every scheduled backup on the server until the
    /// next restart — the same outcome as never having scheduled it.
    /// </remarks>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new BackupRunRequested());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogPublishFailed(_logger, exception);
        }
    }
}
