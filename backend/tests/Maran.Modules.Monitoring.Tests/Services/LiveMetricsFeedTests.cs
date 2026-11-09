using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Options;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The shared live feed: what a reconnecting viewer is back-filled with, what a slow viewer is spared,
/// and when polling is wanted at all.
/// </summary>
public sealed class LiveMetricsFeedTests
{
    /// <summary>The longest any test waits for a reading that is supposed to arrive, so a failure is not a hang.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>Readings the ring holds are returned oldest first, and stored samples are not consulted for them.</summary>
    [Fact]
    public async Task Readings_the_ring_holds_are_returned_oldest_first_without_consulting_stored_samples()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        var now = clock.UtcNow;

        // A stored sample inside the window that the ring already covers: if the feed consulted storage
        // for a window the ring reaches, it would appear here, and it must not.
        dbContext.Samples.Add(Sample(now.AddSeconds(-3), cpu: 99, rx: 0));
        await dbContext.SaveChangesAsync();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;

        feed.Publish(LiveReadings.At(now.AddSeconds(-4), 1));
        feed.Publish(LiveReadings.At(now.AddSeconds(-2), 2));
        feed.Publish(LiveReadings.At(now, 3));

        var history = await feed.GetHistoryAsync(now.AddSeconds(-4), CancellationToken.None);

        Assert.Equal([1d, 2d, 3d], history.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
    }

    /// <summary>A panel that has just started is back-filled from stored samples, the first without a rate.</summary>
    [Fact]
    public async Task A_cold_start_is_back_filled_from_stored_samples_with_rates_derived_between_them()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        var now = clock.UtcNow;
        dbContext.Samples.Add(Sample(now.AddMinutes(-3), cpu: 10, rx: 1_000));
        dbContext.Samples.Add(Sample(now.AddMinutes(-2), cpu: 20, rx: 7_000));
        dbContext.Samples.Add(Sample(now.AddMinutes(-1), cpu: 30, rx: 7_600));
        await dbContext.SaveChangesAsync();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;

        var history = await feed.GetHistoryAsync(now.AddHours(-1), CancellationToken.None);

        Assert.Equal([10d, 20d, 30d], history.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
        Assert.Null(history[0].Network.ReceiveBytesPerSecond);
        Assert.Equal(100d, history[1].Network.ReceiveBytesPerSecond);
        Assert.Equal(10d, history[2].Network.ReceiveBytesPerSecond);

        // The stored sample recorded only the one-minute load: the others are absent, not zero.
        Assert.Equal(0.7, history[0].Cpu.LoadAverage1m);
        Assert.Null(history[0].Cpu.LoadAverage5m);
        Assert.Null(history[0].Cpu.LoadAverage15m);
    }

    /// <summary>Stored samples fill only the part of the window the ring does not reach.</summary>
    [Fact]
    public async Task Stored_samples_fill_only_the_part_of_the_window_the_ring_does_not_reach()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        var now = clock.UtcNow;
        dbContext.Samples.Add(Sample(now.AddMinutes(-2), cpu: 10, rx: 0));
        dbContext.Samples.Add(Sample(now.AddSeconds(-10), cpu: 99, rx: 0));
        await dbContext.SaveChangesAsync();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;
        feed.Publish(LiveReadings.At(now.AddSeconds(-30), 50));

        var history = await feed.GetHistoryAsync(now.AddHours(-1), CancellationToken.None);

        Assert.Equal([10d, 50d], history.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
    }

    /// <summary>A request older than the ring's hour is clamped to it.</summary>
    [Fact]
    public async Task A_request_older_than_the_rings_hour_is_clamped_to_it()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        var now = clock.UtcNow;
        dbContext.Samples.Add(Sample(now.AddHours(-2), cpu: 11, rx: 0));
        dbContext.Samples.Add(Sample(now.AddMinutes(-30), cpu: 22, rx: 0));
        await dbContext.SaveChangesAsync();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;

        var history = await feed.GetHistoryAsync(now.AddDays(-30), CancellationToken.None);

        Assert.Equal([22d], history.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
    }

    /// <summary>A back-fill never exceeds the ring's capacity and keeps the newest readings.</summary>
    [Fact]
    public async Task A_back_fill_never_exceeds_the_rings_capacity_and_keeps_the_newest_readings()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        var now = clock.UtcNow;
        using var harness = NewHarness(dbContext, clock, ringCapacity: 3);
        var feed = harness.Feed;
        for (var index = 0; index < 5; index++)
        {
            feed.Publish(LiveReadings.At(now.AddSeconds(index - 5), index));
        }

        var history = await feed.GetHistoryAsync(now.AddHours(-1), CancellationToken.None);

        Assert.Equal([2d, 3d, 4d], history.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
    }

    /// <summary>Every subscriber receives a published reading.</summary>
    [Fact]
    public async Task Every_subscriber_receives_a_published_reading()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;
        using var first = feed.Subscribe();
        using var second = feed.Subscribe();

        feed.Publish(LiveReadings.At(clock.UtcNow, 7));

        Assert.Equal(7d, (await FirstAsync(first)).Cpu.TotalPercent);
        Assert.Equal(7d, (await FirstAsync(second)).Cpu.TotalPercent);
    }

    /// <summary>A subscriber that reads slower than the feed publishes sees the latest readings and no queue.</summary>
    [Fact]
    public async Task A_slow_subscriber_sees_the_latest_readings_and_nothing_queues_without_bound()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;
        var slow = feed.Subscribe();
        for (var index = 0; index < 5; index++)
        {
            feed.Publish(LiveReadings.At(clock.UtcNow.AddSeconds(index), index));
        }

        slow.Dispose();
        var received = new List<double>();
        await foreach (var reading in slow.ReadAllAsync(CancellationToken.None))
        {
            received.Add(reading.Cpu.TotalPercent);
        }

        // Two, the channel's bound, and the NEWEST two: the first three were dropped, oldest first.
        Assert.Equal([3d, 4d], received);
    }

    /// <summary>A disposed subscription receives nothing more, while a live one still does.</summary>
    [Fact]
    public async Task A_disposed_subscription_receives_nothing_more_while_a_live_one_still_does()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        using var harness = NewHarness(dbContext, clock);
        var feed = harness.Feed;
        var gone = feed.Subscribe();
        using var staying = feed.Subscribe();
        gone.Dispose();

        feed.Publish(LiveReadings.At(clock.UtcNow, 5));

        var goneReceived = new List<double>();
        await foreach (var reading in gone.ReadAllAsync(CancellationToken.None))
        {
            goneReceived.Add(reading.Cpu.TotalPercent);
        }

        Assert.Empty(goneReceived);
        Assert.Equal(5d, (await FirstAsync(staying)).Cpu.TotalPercent);
    }

    /// <summary>Polling is wanted while someone watches and for the idle period after, and not otherwise.</summary>
    [Fact]
    public async Task Polling_is_wanted_while_someone_watches_and_for_the_idle_period_after_and_not_otherwise()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var clock = new FakeClock();
        using var harness = NewHarness(dbContext, clock, idleStopSeconds: 30);
        var feed = harness.Feed;

        // The inverse control: a feed nobody has ever subscribed to is not wanted.
        Assert.False(feed.IsDemanded());

        var subscription = feed.Subscribe();
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(feed.IsDemanded());

        subscription.Dispose();
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.True(feed.IsDemanded());

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(feed.IsDemanded());
    }

    /// <summary>The wait for a first subscriber stays pending until one arrives, then completes.</summary>
    [Fact]
    public async Task The_wait_for_a_first_subscriber_stays_pending_until_one_arrives()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var harness = NewHarness(dbContext, new FakeClock());
        var feed = harness.Feed;

        var waiting = feed.WaitForSubscriberAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        using var subscription = feed.Subscribe();

        await waiting.WaitAsync(Deadline);
        Assert.True(feed.WaitForSubscriberAsync(CancellationToken.None).IsCompleted);
    }

    /// <summary>Builds a feed over an in-memory store, with the scope factory it reads through.</summary>
    /// <param name="dbContext">The store the stored samples are read from.</param>
    /// <param name="clock">The clock the feed dates registrations with.</param>
    /// <param name="ringCapacity">How many readings the ring holds.</param>
    /// <param name="idleStopSeconds">How long polling is wanted after the last subscriber leaves.</param>
    /// <returns>The feed and the container behind it, disposed together.</returns>
    private static FeedHarness NewHarness(
        MonitoringDbContext dbContext,
        FakeClock clock,
        int ringCapacity = 1_800,
        int idleStopSeconds = 30)
    {
        return new FeedHarness(dbContext, clock, ringCapacity, idleStopSeconds);
    }

    /// <summary>Reads the first reading a subscription delivers, bounded so a missing one fails rather than hangs.</summary>
    /// <param name="subscription">The subscription to read.</param>
    /// <returns>The first reading.</returns>
    private static async Task<LiveMetricsReading> FirstAsync(
        ILiveMetricsSubscription subscription)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        await foreach (var reading in subscription.ReadAllAsync(deadline.Token))
        {
            return reading;
        }

        throw new InvalidOperationException("The subscription ended without delivering a reading.");
    }

    /// <summary>One stored one-minute sample.</summary>
    /// <param name="at">When it was captured.</param>
    /// <param name="cpu">Its processor utilisation.</param>
    /// <param name="rx">Its received-bytes counter.</param>
    /// <returns>The sample.</returns>
    private static MetricSample Sample(DateTimeOffset at, double cpu, long rx)
    {
        return new MetricSample(at, cpu, 100, 1_000, 300, 200, 1_000, rx, 0, 0.7);
    }

    /// <summary>A feed together with the container its scope factory comes from.</summary>
    private sealed class FeedHarness : IDisposable
    {
        /// <summary>The container that owns the store registration the feed reads through.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>The feed under test.</summary>
        public LiveMetricsFeed Feed { get; }

        /// <summary>Builds the feed over <paramref name="dbContext"/>.</summary>
        /// <param name="dbContext">The store the stored samples are read from.</param>
        /// <param name="clock">The clock the feed dates registrations with.</param>
        /// <param name="ringCapacity">How many readings the ring holds.</param>
        /// <param name="idleStopSeconds">How long polling is wanted after the last subscriber leaves.</param>
        public FeedHarness(MonitoringDbContext dbContext, FakeClock clock, int ringCapacity, int idleStopSeconds)
        {
            var services = new ServiceCollection();
            services.AddSingleton(dbContext);
            _provider = services.BuildServiceProvider();

            Feed = new LiveMetricsFeed(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                new OptionsWrapper<LiveMetricsOptions>(
                    new LiveMetricsOptions { RingCapacity = ringCapacity, IdleStopSeconds = idleStopSeconds }));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }
    }
}
