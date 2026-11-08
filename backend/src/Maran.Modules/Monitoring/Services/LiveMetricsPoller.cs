using Maran.Agent.Client.Interfaces;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Options;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Asks the agent for one host reading every few seconds, but only while someone is watching, and
/// publishes it to <see cref="LiveMetricsFeed"/>. The single caller of the agent for the live dashboard.
/// </summary>
/// <remarks>
/// <para>
/// <b>Demand-driven.</b> With no registration the loop waits and the agent is not called; it starts on
/// the first registration and stops <see cref="LiveMetricsOptions.IdleStopSeconds"/> after the last one
/// leaves (owner decision D2). A round is awaited before the next timer tick is taken, and a
/// <see cref="PeriodicTimer"/> coalesces ticks that elapsed meanwhile, so a round that overruns the
/// interval makes the next one start late rather than overlap it.
/// </para>
/// <para>
/// <b>A failed round publishes nothing, as <see cref="MetricsSampler"/>'s does.</b> A row of zeroes is a
/// claim about the machine; a missing point is the honest record of a moment the panel could not see.
/// The rate of the next successful round divides by the seconds that really elapsed since the last
/// reading it holds, so a gap does not turn into a spike.
/// </para>
/// <para>
/// <b>Rates restart after idleness.</b> The remembered counters are dropped whenever the loop goes to
/// sleep: a rate averaged across ten idle minutes is a number no one measured, so the first reading
/// after a wake-up has no rate.
/// </para>
/// <para>
/// The cadence lives in <see cref="LiveMetricsOptions"/>; the hosted-service registration is the Host's
/// (<c>BackgroundWorkExtensions</c>), as for <see cref="MetricsSampler"/>, because a module may not
/// register one.
/// </para>
/// </remarks>
public sealed class LiveMetricsPoller : BackgroundService
{
    /// <summary>Pre-compiled log delegate for a round the agent would not complete.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRoundSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(LiveMetricsPoller)),
            "Skipped a live reading; the agent did not answer ({Code})");

    /// <summary>Pre-compiled log delegate for a round that failed for a reason other than the agent.</summary>
    private static readonly Action<ILogger, Exception?> LogRoundFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(LiveMetricsPoller)),
            "A live reading could not be taken; the dashboard will show a gap for this interval");

    /// <summary>The feed the readings go to, and which says whether anyone is watching.</summary>
    private readonly LiveMetricsFeed _feed;

    /// <summary>The agent, the only thing that can see the host.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>The panel's clock, which dates every reading.</summary>
    private readonly IClock _clock;

    /// <summary>The validated settings.</summary>
    private readonly LiveMetricsOptions _options;

    /// <summary>Where a skipped or failed round is reported.</summary>
    private readonly ILogger<LiveMetricsPoller> _logger;

    /// <summary>The previous successful reading's counters, or <c>null</c> after idleness or at start.</summary>
    private PreviousCounters? _previous;

    /// <summary>Creates the poller.</summary>
    /// <param name="feed">The feed to publish to.</param>
    /// <param name="agent">The agent client.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="options">The validated settings.</param>
    /// <param name="logger">Where a skipped or failed round is reported.</param>
    public LiveMetricsPoller(
        LiveMetricsFeed feed,
        IAgentMonitorClient agent,
        IClock clock,
        IOptions<LiveMetricsOptions> options,
        ILogger<LiveMetricsPoller> logger)
    {
        _feed = feed;
        _agent = agent;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Takes one reading and publishes it.</summary>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>True when a reading was published; false when the agent did not answer.</returns>
    /// <remarks>
    /// Public because it is the round and the round has the behaviour worth asserting: a test drives it
    /// directly instead of starting a hosted service and waiting for a timer.
    /// </remarks>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        var metrics = await _agent.GetHostMetricsAsync(cancellationToken);
        if (!metrics.IsSuccess)
        {
            LogRoundSkipped(_logger, metrics.Error!.Code, null);
            return false;
        }

        var at = _clock.UtcNow;
        var value = metrics.Value;
        var rx = ToSignedBytes(value.NetworkRxBytes);
        var tx = ToSignedBytes(value.NetworkTxBytes);

        var rate = _previous is null
            ? null
            : NetworkRateCalculator.RateBetween(_previous.At, _previous.Rx, _previous.Tx, at, rx, tx);
        _previous = new PreviousCounters(at, rx, tx);

        _feed.Publish(new LiveMetricsReading(
            at,
            new LiveCpuReading(value.CpuPercent, value.LoadAverage1m, value.LoadAverage5m, value.LoadAverage15m),
            new LiveMemoryReading(ToSignedBytes(value.MemoryUsedBytes), ToSignedBytes(value.MemoryTotalBytes)),
            new LiveDiskReading(ToSignedBytes(value.DiskUsedBytes), ToSignedBytes(value.DiskTotalBytes)),
            new LiveNetworkReading(rate?.ReceivedBytesPerSecond, rate?.SentBytesPerSecond)));
        return true;
    }

    /// <summary>Polls while someone is watching, sleeps while no one is, until the host shuts down.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _feed.WaitForSubscriberAsync(stoppingToken);

                _previous = null;
                using var timer = new PeriodicTimer(_options.Interval);
                do
                {
                    await RoundAsync(stoppingToken);
                }
                while (_feed.IsDemanded() && await timer.WaitForNextTickAsync(stoppingToken));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and not logged as one.
        }
    }

    /// <summary>Runs one round, turning any failure into a log line rather than the end of the loop.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    private async Task RoundAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PollOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogRoundFailed(_logger, exception);
        }
    }

    /// <summary>Narrows an unsigned byte count onto the signed one the contract carries.</summary>
    /// <param name="value">The figure the agent reported.</param>
    /// <returns>The same value, saturated at <see cref="long.MaxValue"/> (see <c>MetricsSampler</c>).</returns>
    private static long ToSignedBytes(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    /// <summary>The counters and instant of the previous reading, which the next rate is measured from.</summary>
    /// <param name="At">When the reading was taken.</param>
    /// <param name="Rx">The received-bytes counter.</param>
    /// <param name="Tx">The sent-bytes counter.</param>
    private sealed record PreviousCounters(DateTimeOffset At, long Rx, long Tx);
}
