using Maran.Modules.Monitoring.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="SampleRetentionRequested"/> once a day, which is the only thing that makes
/// the Monitoring module's retention sweep actually run.
/// </summary>
/// <remarks>
/// Without it <c>monitoring.Samples</c> grows for as long as the panel does — one row a minute,
/// forever — because nothing else ever removes one. The module owns the JOB (it decides the seven-day
/// window and does the deleting) and this owns the CADENCE, mirroring
/// <see cref="TaskRetentionScheduler"/> exactly.
///
/// It is a hosted service inside <c>maran-api</c>, not a new process: rules/architecture.md fixes the
/// system at three processes and rules/security.md item 10 forbids a new daemon without a spec
/// change. The work itself is a durable Wolverine message, so a pass that fails is visible and
/// retriable where every other failed message is.
///
/// The startup delay is offset from the other two daily passes' five and ten minutes, so a fresh
/// boot does not start every daily pass in the same instant and contend for the same database
/// connections.
/// </remarks>
public sealed class MetricsRetentionScheduler : BackgroundService
{
    /// <summary>How long between retention passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>How long after startup the first pass runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(15);

    /// <summary>Pre-compiled log delegate for a pass this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(MetricsRetentionScheduler)),
            "Could not queue the daily monitoring-sample retention pass; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>IMessageBus</c>: a <see cref="BackgroundService"/> is a singleton,
    /// Wolverine registers <c>IMessageBus</c> as scoped, and capturing it directly is refused by the
    /// container at BUILD time rather than degrading gracefully at runtime.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the pass is reported.</summary>
    private readonly ILogger<MetricsRetentionScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the pass is reported.</param>
    public MetricsRetentionScheduler(IServiceScopeFactory scopeFactory, ILogger<MetricsRetentionScheduler> logger)
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
    /// the process, so one bad night would silently end retention until the next restart — the same
    /// outcome as never having scheduled it.
    /// </remarks>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new SampleRetentionRequested());
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
