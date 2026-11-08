using System.Threading.Channels;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Options;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// The panel's one shared feed of live host readings: <see cref="LiveMetricsPoller"/> publishes one
/// reading per tick, and every dashboard stream receives it from here. Holds the ring of the last hour
/// and the registrations that decide whether anyone is watching.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cost is independent of the number of viewers.</b> The feed never calls the agent itself; the
/// poller does, once per tick, and only while <see cref="IsDemanded"/> says someone is watching. Ten
/// tabs therefore cost the agent what one does, and a panel nobody has open costs it nothing (owner
/// decision D2: on-demand sampling).
/// </para>
/// <para>
/// <b>A slow viewer sees the latest reading, never a growing queue.</b> Each registration owns a
/// channel bounded at two readings that drops the oldest on overflow, so the memory a stalled client
/// can hold is two readings, not however many ticks passed while it was stalled.
/// </para>
/// <para>
/// <b>Back-fill is the ring, then stored samples for what the ring does not reach.</b> A panel that
/// has just started, or a viewer that asks for more than the ring holds, is answered from the stored
/// one-minute samples (<c>monitoring.Samples</c>). Those points are coarser and carry no five- or
/// fifteen-minute load; the answer does not say so, because a coarser point is still a true one.
/// </para>
/// <para>
/// A singleton: it holds state across requests and reads the database through a scope of its own per
/// call, since the context is scoped and a singleton may not capture one.
/// </para>
/// </remarks>
public sealed class LiveMetricsFeed : ILiveMetricsFeed
{
    /// <summary>How far back a history request reaches: the ring's hour, the issue's figure.</summary>
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(1);

    /// <summary>Guards the ring, the registrations and the demand bookkeeping.</summary>
    private readonly object _gate = new();

    /// <summary>The last hour of readings.</summary>
    private readonly MetricsRing _ring;

    /// <summary>Every live registration.</summary>
    private readonly List<Subscription> _subscriptions = [];

    /// <summary>Opens the scope the stored-sample read resolves its context from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The panel's clock, which dates registrations and clamps a history request.</summary>
    private readonly IClock _clock;

    /// <summary>The validated settings.</summary>
    private readonly LiveMetricsOptions _options;

    /// <summary>Completed when a registration arrives; replaced once the poller has consumed it.</summary>
    private TaskCompletionSource _demandSignal = NewDemandSignal();

    /// <summary>When a registration was last created or removed.</summary>
    private DateTimeOffset _lastDemandAt;

    /// <summary>Creates the feed.</summary>
    /// <param name="scopeFactory">Opens the scope the stored-sample read uses.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="options">The validated settings.</param>
    public LiveMetricsFeed(IServiceScopeFactory scopeFactory, IClock clock, IOptions<LiveMetricsOptions> options)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _ring = new MetricsRing(_options.RingCapacity);
        _lastDemandAt = DateTimeOffset.MinValue;
    }

    /// <inheritdoc />
    public ILiveMetricsSubscription Subscribe()
    {
        var subscription = new Subscription(this);

        lock (_gate)
        {
            _subscriptions.Add(subscription);
            _lastDemandAt = _clock.UtcNow;
            _demandSignal.TrySetResult();
        }

        return subscription;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveMetricsReading>> GetHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var floor = since < now - HistoryWindow ? now - HistoryWindow : since;

        IReadOnlyList<LiveMetricsReading> fromRing;
        DateTimeOffset? ringOldest;
        lock (_gate)
        {
            fromRing = _ring.Since(floor);
            ringOldest = _ring.OldestAt();
        }

        // The ring already reaches back to the wanted instant: nothing stored is needed.
        if (ringOldest is { } oldest && oldest <= floor)
        {
            return Capped(fromRing);
        }

        var before = fromRing.Count > 0 ? fromRing[0].At : now;
        var stored = await ReadStoredAsync(floor, before, cancellationToken);

        var combined = new List<LiveMetricsReading>(stored.Count + fromRing.Count);
        combined.AddRange(stored);
        combined.AddRange(fromRing);
        return Capped(combined);
    }

    /// <summary>Adds one reading to the ring and hands it to every registration.</summary>
    /// <param name="reading">The reading the poller took.</param>
    public void Publish(LiveMetricsReading reading)
    {
        Subscription[] receivers;
        lock (_gate)
        {
            _ring.Add(reading);
            receivers = [.. _subscriptions];
        }

        foreach (var receiver in receivers)
        {
            receiver.Offer(reading);
        }
    }

    /// <summary>Whether anyone is watching, or was within the idle period.</summary>
    /// <returns>
    /// True while a registration exists, and for <see cref="LiveMetricsOptions.IdleStop"/> after the last
    /// one was removed.
    /// </returns>
    public bool IsDemanded()
    {
        lock (_gate)
        {
            return _subscriptions.Count > 0 || _clock.UtcNow - _lastDemandAt < _options.IdleStop;
        }
    }

    /// <summary>Completes once at least one registration exists.</summary>
    /// <param name="cancellationToken">Cancelled when the host shuts down.</param>
    /// <returns>Resolves when there is someone to poll for.</returns>
    public Task WaitForSubscriberAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_subscriptions.Count > 0)
            {
                return Task.CompletedTask;
            }

            if (_demandSignal.Task.IsCompleted)
            {
                _demandSignal = NewDemandSignal();
            }

            return _demandSignal.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Creates the signal the poller waits on while nobody is watching.</summary>
    /// <returns>An uncompleted signal whose continuations run off the publisher's thread.</returns>
    private static TaskCompletionSource NewDemandSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Keeps the newest readings when more than the ring holds were gathered.</summary>
    /// <param name="readings">The gathered readings, oldest first.</param>
    /// <returns>At most <see cref="LiveMetricsOptions.RingCapacity"/> readings, the newest.</returns>
    private IReadOnlyList<LiveMetricsReading> Capped(IReadOnlyList<LiveMetricsReading> readings)
    {
        if (readings.Count <= _options.RingCapacity)
        {
            return readings;
        }

        return readings.Skip(readings.Count - _options.RingCapacity).ToList();
    }

    /// <summary>Reads stored one-minute samples in <c>[earliest, before)</c> as live readings.</summary>
    /// <param name="earliest">The earliest sample wanted.</param>
    /// <param name="before">The instant the ring's own readings begin.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The samples, oldest first, with a rate derived for every one but the first.</returns>
    private async Task<IReadOnlyList<LiveMetricsReading>> ReadStoredAsync(
        DateTimeOffset earliest,
        DateTimeOffset before,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();

        // An hour of one-minute samples is about sixty rows; the take is the bound, not an estimate.
        var rows = await dbContext.Samples
            .AsNoTracking()
            .Where(sample => sample.CapturedAt >= earliest && sample.CapturedAt < before)
            .OrderBy(sample => sample.CapturedAt)
            .Take(_options.RingCapacity)
            .ToListAsync(cancellationToken);

        var readings = new List<LiveMetricsReading>(rows.Count);
        MetricSample? previous = null;
        foreach (var row in rows)
        {
            readings.Add(ToReading(row, previous));
            previous = row;
        }

        return readings;
    }

    /// <summary>Rebuilds a live reading from a stored sample.</summary>
    /// <param name="sample">The stored sample.</param>
    /// <param name="previous">The sample before it, or <c>null</c> for the first.</param>
    /// <returns>The reading; the figures the sample never recorded are absent, not zero.</returns>
    private static LiveMetricsReading ToReading(MetricSample sample, MetricSample? previous)
    {
        var rate = previous is null
            ? null
            : NetworkRateCalculator.RateBetween(
                previous.CapturedAt,
                previous.NetworkRxBytes,
                previous.NetworkTxBytes,
                sample.CapturedAt,
                sample.NetworkRxBytes,
                sample.NetworkTxBytes);

        return new LiveMetricsReading(
            sample.CapturedAt,
            new LiveCpuReading(sample.CpuPercent, sample.LoadAverage1m, null, null),
            new LiveMemoryReading(sample.MemoryUsedBytes, sample.MemoryTotalBytes),
            new LiveDiskReading(sample.DiskInUseBytes, sample.DiskTotalBytes),
            new LiveNetworkReading(rate?.ReceivedBytesPerSecond, rate?.SentBytesPerSecond));
    }

    /// <summary>Removes a registration and starts the idle period.</summary>
    /// <param name="subscription">The registration being disposed.</param>
    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            if (_subscriptions.Remove(subscription))
            {
                _lastDemandAt = _clock.UtcNow;
            }
        }
    }

    /// <summary>One consumer's registration: a two-reading channel that keeps the latest.</summary>
    private sealed class Subscription : ILiveMetricsSubscription
    {
        /// <summary>The feed that unregisters this on disposal.</summary>
        private readonly LiveMetricsFeed _feed;

        /// <summary>Bounded at two readings; the oldest is dropped when a third arrives.</summary>
        private readonly Channel<LiveMetricsReading> _channel = Channel.CreateBounded<LiveMetricsReading>(
            new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        /// <summary>Creates a registration for <paramref name="feed"/>.</summary>
        /// <param name="feed">The feed to unregister from.</param>
        public Subscription(LiveMetricsFeed feed)
        {
            _feed = feed;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LiveMetricsReading> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        /// <summary>Hands the registration one reading; a full channel drops its oldest.</summary>
        /// <param name="reading">The reading to deliver.</param>
        public void Offer(LiveMetricsReading reading)
        {
            _channel.Writer.TryWrite(reading);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _feed.Remove(this);
            _channel.Writer.TryComplete();
        }
    }
}
