using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Asks the agent for one reading of the host about once a minute, stores it, and hands it to the
/// alert evaluator. Everything the panel's charts draw comes from this loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failed round writes nothing, and that is the design rather than an omission.</b> The agent
/// can be restarting, upgrading, or briefly unreachable; the panel then has no numbers for that
/// minute, and the honest record of that is a missing row. A row of zeroes would be a claim about
/// the machine — no memory in use, no traffic, no load — and every chart would draw it as one.
/// A gap is also why R7's rate arithmetic divides by measured time: the sampler is ALLOWED to miss,
/// so no reader may assume the interval.
/// </para>
/// <para>
/// <b>Service statuses are best-effort within a round that otherwise succeeded.</b> If the metrics
/// came back and the statuses did not, the sample is still stored — the chart data is real — and the
/// evaluator is simply given no services to judge, which advances no alert counter in either
/// direction.
/// </para>
/// <para>
/// <b>The loop never lets one bad round end it.</b> An exception escaping
/// <see cref="ExecuteAsync"/> stops a hosted service for the lifetime of the process, so a single
/// transient failure would silently end monitoring until somebody restarted the panel — and the only
/// symptom would be charts that stop at a timestamp nobody notices for a week.
/// </para>
/// <para>
/// <b>It lives in the module and its cadence lives in the Host.</b> A module may not reference the
/// Host, so it cannot register its own hosted service; the Host's
/// <c>BackgroundWorkExtensions</c> owns that line. The same split
/// <c>StartupBanReconciler</c> uses, and the right one — how often a server is sampled is a
/// deployment decision.
/// </para>
/// </remarks>
public sealed class MetricsSampler : BackgroundService
{
    /// <summary>How long between readings.</summary>
    /// <remarks>
    /// Sixty seconds, which is what R10's retention arithmetic is sized for: about 1,440 rows a day
    /// and about 10,080 over the seven-day window. It is a nominal cadence and not a guarantee — see
    /// the type's remarks on gaps.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How long after startup the first reading is taken.</summary>
    /// <remarks>
    /// Short, unlike the daily passes' ten-minute offset: a freshly started panel with an empty chart
    /// is the state an operator is most likely to be looking at, and the agent handshake has already
    /// happened by the time hosted services run.
    /// </remarks>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>Pre-compiled log delegate for a round the agent would not complete.</summary>
    /// <remarks>
    /// Debug, not warning. An unreachable agent is expected during its own restarts, and this line
    /// would otherwise appear once a minute for as long as an upgrade takes — which is how a log
    /// stops being read. The agent's own health has its own probe.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogRoundSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(MetricsSampler)),
            "Skipped a monitoring sample; the agent did not answer ({Code})");

    /// <summary>Pre-compiled log delegate for a round that failed for a reason other than the agent.</summary>
    private static readonly Action<ILogger, Exception?> LogRoundFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(MetricsSampler)),
            "A monitoring sample could not be stored; the charts will show a gap for this interval");

    /// <summary>Opens one scope per round to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <c>MonitoringDbContext</c>. A <see cref="BackgroundService"/> is a
    /// singleton, the context is scoped, and a singleton capturing a scoped dependency is refused by
    /// the container at BUILD time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a skipped or failed round is reported.</summary>
    private readonly ILogger<MetricsSampler> _logger;

    /// <summary>Creates the sampler.</summary>
    /// <param name="scopeFactory">Opens the scope each round resolves its dependencies from.</param>
    /// <param name="clock">The panel's clock, which timestamps every sample.</param>
    /// <param name="logger">Where a skipped or failed round is reported.</param>
    public MetricsSampler(IServiceScopeFactory scopeFactory, IClock clock, ILogger<MetricsSampler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Takes one reading, stores it, and evaluates the alerts against it.</summary>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>
    /// True when a sample was stored; false when the agent did not answer and the round became a gap.
    /// </returns>
    /// <remarks>
    /// Public because it is the round, and the round is what has behaviour worth asserting: a test
    /// drives it directly rather than starting a hosted service and waiting for a timer, which is the
    /// sleep rules/testing.md forbids.
    /// </remarks>
    public async Task<bool> SampleOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentMonitorClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<AlertEvaluator>();

        var metrics = await agent.GetHostMetricsAsync(cancellationToken);
        if (!metrics.IsSuccess)
        {
            LogRoundSkipped(_logger, metrics.Error!.Code, null);
            return false;
        }

        var observedAt = _clock.UtcNow;
        dbContext.Samples.Add(ToSample(metrics.Value, observedAt));
        await dbContext.SaveChangesAsync(cancellationToken);

        var statuses = await agent.GetServiceStatusesAsync(cancellationToken);
        var services = statuses.IsSuccess ? statuses.Value : [];

        await evaluator.EvaluateAsync(
            DiskUsedPercent(metrics.Value), services, observedAt, cancellationToken);

        return true;
    }

    /// <summary>Projects one agent reading onto the row that is stored.</summary>
    /// <param name="metrics">The reading the agent returned.</param>
    /// <param name="observedAt">When it was taken, from the panel's clock.</param>
    /// <returns>The sample to store.</returns>
    private static MetricSample ToSample(AgentHostMetrics metrics, DateTimeOffset observedAt)
    {
        return new MetricSample(
            observedAt,
            metrics.CpuPercent,
            ToSignedBytes(metrics.MemoryUsedBytes),
            ToSignedBytes(metrics.MemoryTotalBytes),
            ToSignedBytes(metrics.DiskUsedBytes),
            ToSignedBytes(metrics.DiskTotalBytes),
            ToSignedBytes(metrics.NetworkRxBytes),
            ToSignedBytes(metrics.NetworkTxBytes),
            metrics.LoadAverage1m);
    }

    /// <summary>Narrows an unsigned byte count onto the signed one PostgreSQL stores.</summary>
    /// <param name="value">The figure the agent reported.</param>
    /// <returns>The same value, saturated at <see cref="long.MaxValue"/>.</returns>
    /// <remarks>
    /// PostgreSQL has no unsigned integer type, so a <c>bigint</c> column is the widest honest home
    /// for a <c>uint64</c>. Saturating rather than casting: an unchecked cast turns a value above
    /// <see cref="long.MaxValue"/> into a NEGATIVE byte count, which would draw a chart below zero
    /// and make the network rate arithmetic clamp a perfectly ordinary interval to nothing. The
    /// ceiling is over nine exabytes — no reading will reach it — so this is about what the type
    /// system permits, not about what the machine will report.
    /// </remarks>
    private static long ToSignedBytes(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    /// <summary>How full the root filesystem is, or <c>null</c> when the agent reported no capacity.</summary>
    /// <param name="metrics">The reading the agent returned.</param>
    /// <returns>The percentage, or <c>null</c> when it cannot be computed.</returns>
    /// <remarks>
    /// A zero capacity is not a full disk and it is not an empty one — it is a filesystem the agent
    /// could not measure. Dividing by it would produce an infinity that compares greater than every
    /// threshold, so the panel would mail about a disk emergency on a host whose disk it cannot see.
    /// </remarks>
    private static double? DiskUsedPercent(AgentHostMetrics metrics)
    {
        if (metrics.DiskTotalBytes == 0)
        {
            return null;
        }

        return metrics.DiskUsedBytes * 100.0 / metrics.DiskTotalBytes;
    }

    /// <summary>Samples on the interval until the host shuts down.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RoundAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one: a hosted service that
            // reported an error on every clean stop trains an operator to ignore its errors.
        }
    }

    /// <summary>Runs one round, turning any failure into a log line rather than the end of the loop.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    private async Task RoundAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SampleOnceAsync(stoppingToken);
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
}
