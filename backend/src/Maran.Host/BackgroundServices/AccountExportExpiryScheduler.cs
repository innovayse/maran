using Maran.Modules.ServerMigration.Jobs;
using Wolverine;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Publishes <see cref="AccountExportExpiryRequested"/> once an hour, which is the only thing that makes the
/// ServerMigration module remove an account export when its retention ends (issue #127, decision D11).
/// </summary>
/// <remarks>
/// Hourly and not daily because the screen promises a package is gone after seven days, and a daily pass would let a
/// plaintext copy of a customer's data outlive that promise by a day. The module owns the JOB (what has expired, the
/// order of removal) and this owns the CADENCE; without this line the artifacts of finished exports stay on disk
/// until their account is deleted. A hosted service inside <c>maran-api</c>, not a new process
/// (rules/security.md item 10); the work is a durable Wolverine message, so a failed pass is visible and retriable.
/// The first pass runs after a delay that differs from the other schedulers' so a fresh boot does not start them
/// all in one instant.
/// </remarks>
public sealed class AccountExportExpiryScheduler : BackgroundService
{
    /// <summary>How long between passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How long after startup the first pass runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(7);

    /// <summary>Pre-compiled log delegate for a pass this scheduler could not publish.</summary>
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(AccountExportExpiryScheduler)),
            "Could not queue the hourly account-export expiry pass; it will be retried at the next interval");

    /// <summary>Opens one scope per attempt to resolve the message bus from (it is scoped; this service is a singleton).</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failure to queue the pass is reported.</summary>
    private readonly ILogger<AccountExportExpiryScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="scopeFactory">Opens the scope each attempt resolves the message bus from.</param>
    /// <param name="logger">Where a failure to queue the pass is reported.</param>
    public AccountExportExpiryScheduler(IServiceScopeFactory scopeFactory, ILogger<AccountExportExpiryScheduler> logger)
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
            // Shutdown. Not a failure, and not logged as one.
        }
    }

    /// <summary>Publishes one trigger, turning a bus failure into a log line rather than a crash.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    private async Task PublishAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await bus.PublishAsync(new AccountExportExpiryRequested());
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
