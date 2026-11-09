using Maran.Modules.Files.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="TrashRetentionRequested"/> once a day, which is the only thing that removes an
/// entry from a customer's file-manager trash once its retention has passed.
/// </summary>
/// <remarks>
/// Without it a trashed entry stays for ever and keeps counting toward its account's quota, because the
/// agent keeps no schedule. The module owns the JOB (which accounts, what a failing account does to the
/// rest) and this owns the CADENCE, mirroring <see cref="SiteLogMaintenanceScheduler"/> exactly. It is a
/// hosted service inside <c>maran-api</c>, not a new process (rules/architecture.md fixes the system at
/// three), and the work itself is a Wolverine message. The purge is idempotent, so a restart that runs it
/// twice in a day removes nothing the first run left.
///
/// The startup delay is offset from the other schedulers', so a fresh boot does not start every pass in
/// the same instant and contend for the same connections.
/// </remarks>
public sealed class TrashRetentionScheduler : BackgroundService
{
    /// <summary>How long between retention passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>How long after startup the first pass runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(23);

    /// <summary>Pre-compiled log delegate for a pass this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(TrashRetentionScheduler)),
            "Could not queue the daily trash retention pass; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>IMessageBus</c>: a <see cref="BackgroundService"/> is a singleton and
    /// Wolverine registers <c>IMessageBus</c> as scoped.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the pass is reported.</summary>
    private readonly ILogger<TrashRetentionScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the pass is reported.</param>
    public TrashRetentionScheduler(IServiceScopeFactory scopeFactory, ILogger<TrashRetentionScheduler> logger)
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
            // Shutdown. Not a failure, and deliberately not logged as one.
        }
    }

    /// <summary>Publishes one trigger, turning a bus failure into a log line rather than a crash.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <remarks>
    /// An unhandled exception out of <see cref="ExecuteAsync"/> stops the service for the lifetime of the
    /// process, so one bad day would silently end the retention until the next restart.
    /// </remarks>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new TrashRetentionRequested());
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
