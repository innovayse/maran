using Maran.Modules.Ssl.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="CertificateRenewalRequested"/> once a day, which is the only thing that makes
/// certificate renewal actually happen.
/// </summary>
/// <remarks>
/// Without this the Ssl module ships with a renewal job that is fully implemented, fully tested and
/// never runs — so every certificate it issues expires ninety days later with nothing watching, and
/// no test in the suite notices, because what is missing is precisely the schedule.
///
/// Daily is the cadence the thirty-day window is designed around: a ninety-day certificate entering
/// its window has thirty daily opportunities before it expires, so a domain being repaired, an
/// authority having a bad week, or a server that was switched off for a fortnight all still renew.
/// The job's own per-certificate backoff (<c>Certificate.NextAttemptAllowedAt</c>) is what keeps a
/// permanently failing domain from turning this cadence into thirty wasted orders.
///
/// It is a hosted service inside <c>maran-api</c>, not a new process: rules/architecture.md fixes the
/// system at three processes and rules/security.md item 10 forbids a new daemon without a spec
/// change. The work itself is a durable Wolverine message, so a pass that fails is visible and
/// retriable where every other failed message is.
///
/// The first pass runs shortly AFTER startup rather than at it. A panel that is restarted repeatedly
/// — an operator debugging a configuration, a crash loop — would otherwise start a renewal pass on
/// every boot, and each pass costs orders at a shared, rate-limited account.
/// </remarks>
public sealed class CertificateRenewalScheduler : BackgroundService
{
    /// <summary>How long between renewal passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>How long after startup the first pass runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>Pre-compiled log delegate for a pass this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(CertificateRenewalScheduler)),
            "Could not queue the daily certificate renewal pass; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>IMessageBus</c>. A <see cref="BackgroundService"/> is a singleton,
    /// Wolverine registers <c>IMessageBus</c> as scoped, and asking a singleton to capture a scoped
    /// dependency is refused by the container at BUILD time — so the constructor that took the bus
    /// directly did not degrade a feature, it stopped <c>WebApplicationBuilder.Build()</c> and the
    /// whole API with it, deterministically, on every start.
    ///
    /// The scope is opened per attempt rather than once for the lifetime of the service, which is the
    /// point of resolving late: a scope held for the life of the process is a singleton wearing a
    /// different name, and it would keep whatever the bus holds — connections, outbox state — alive
    /// between passes that are a day apart.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the pass is reported.</summary>
    private readonly ILogger<CertificateRenewalScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the pass is reported.</param>
    public CertificateRenewalScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<CertificateRenewalScheduler> logger)
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
    /// the process, so one bad night would silently end renewal until the next restart — which is the
    /// same outcome as never having scheduled it.
    /// </remarks>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new CertificateRenewalRequested());
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
