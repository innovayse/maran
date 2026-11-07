using Maran.Modules.Identity.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="AuditMaintenanceRequested"/> every hour, which is the only thing that makes the
/// Identity module's audit upkeep (sealing the hash chain, retention, the orphan-actor sweep and chain
/// verification) actually run.
/// </summary>
/// <remarks>
/// Without this the module ships with a fully implemented maintenance handler that never runs: the
/// journal would never be sealed, and a retention window an administrator saved would never delete
/// anything. It mirrors <see cref="TaskRetentionScheduler"/> exactly, for the same reasons.
///
/// <b>Hourly, not daily, although retention is daily.</b> Sealing is what the tick is mostly for, and
/// an unsealed row is outside the chain's protection until it is sealed (threat note, section 3), so
/// the interval is the length of that window. The once-a-day work is gated by the handler against the
/// stored time of its last run, which survives a restart; this class owns the cadence and nothing else.
///
/// It is a hosted service inside <c>maran-api</c>, not a new process: rules/architecture.md fixes the
/// system at three processes and rules/security.md item 10 forbids a new daemon without a spec
/// change.
///
/// The first tick runs shortly AFTER startup, at three minutes, which is offset from
/// <see cref="CertificateRenewalScheduler.StartupDelay"/> and <see cref="TaskRetentionScheduler.StartupDelay"/>
/// so a fresh boot does not start every pass in the same instant and contend for database connections.
/// </remarks>
public sealed class AuditMaintenanceScheduler : BackgroundService
{
    /// <summary>How long between maintenance ticks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How long after startup the first tick runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    /// <summary>Pre-compiled log delegate for a tick this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(AuditMaintenanceScheduler)),
            "Could not queue the hourly audit maintenance tick; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>IMessageBus</c>, for the reason <see cref="CertificateRenewalScheduler"/>
    /// states: a <see cref="BackgroundService"/> is a singleton, Wolverine registers <c>IMessageBus</c>
    /// as scoped, and the container refuses to capture it directly at build time.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the tick is reported.</summary>
    private readonly ILogger<AuditMaintenanceScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the tick is reported.</param>
    public AuditMaintenanceScheduler(IServiceScopeFactory scopeFactory, ILogger<AuditMaintenanceScheduler> logger)
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
    /// the process, so one bad hour would silently end sealing until the next restart.
    /// </remarks>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new AuditMaintenanceRequested());
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
