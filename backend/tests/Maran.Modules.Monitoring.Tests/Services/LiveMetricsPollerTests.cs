using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Options;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// One polling round and the demand-driven loop around it: what is published, what is withheld, and
/// that the agent is not asked while nobody is watching.
/// </summary>
public sealed class LiveMetricsPollerTests
{
    /// <summary>The longest any test waits for a reading that is supposed to arrive, so a failure is not a hang.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>A round publishes the agent's figures stamped with the panel's clock, and the first has no rate.</summary>
    [Fact]
    public async Task A_round_publishes_the_agents_figures_with_the_panels_clock_and_the_first_has_no_rate()
    {
        var agent = new StubAgentMonitorClient { Metrics = Ok(rx: 1_000, tx: 2_000, cpu: 42.5) };
        var clock = new FakeClock();
        using var harness = new Harness(agent, clock);
        using var subscription = harness.Feed.Subscribe();

        Assert.True(await harness.Poller.PollOnceAsync(CancellationToken.None));

        var reading = await FirstAsync(subscription);
        Assert.Equal(clock.UtcNow, reading.At);
        Assert.Equal(42.5, reading.Cpu.TotalPercent);
        Assert.Equal(1.5, reading.Cpu.LoadAverage1m);
        Assert.Equal(1.2, reading.Cpu.LoadAverage5m);
        Assert.Equal(1.1, reading.Cpu.LoadAverage15m);
        Assert.Equal(3_000, reading.Memory.UsedBytes);
        Assert.Equal(8_000, reading.Memory.TotalBytes);
        Assert.Equal(50, reading.Disk.UsedBytes);
        Assert.Equal(100, reading.Disk.TotalBytes);
        Assert.Null(reading.Network.ReceiveBytesPerSecond);
        Assert.Null(reading.Network.SendBytesPerSecond);
    }

    /// <summary>The second reading's rate is the counter difference over the seconds that really elapsed.</summary>
    [Fact]
    public async Task The_second_readings_rate_is_the_counter_difference_over_the_seconds_that_elapsed()
    {
        var agent = new StubAgentMonitorClient { Metrics = Ok(rx: 1_000, tx: 0) };
        var clock = new FakeClock();
        using var harness = new Harness(agent, clock);
        using var subscription = harness.Feed.Subscribe();
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(4));
        agent.Metrics = Ok(rx: 1_400, tx: 800);
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        var readings = await TwoAsync(subscription);
        Assert.Equal(100d, readings[1].Network.ReceiveBytesPerSecond);
        Assert.Equal(200d, readings[1].Network.SendBytesPerSecond);
    }

    /// <summary>A counter that went backwards, as after a reboot, is a rate of zero and never a negative one.</summary>
    [Fact]
    public async Task A_counter_that_went_backwards_is_a_rate_of_zero_and_never_a_negative_one()
    {
        var agent = new StubAgentMonitorClient { Metrics = Ok(rx: 9_000, tx: 9_000) };
        var clock = new FakeClock();
        using var harness = new Harness(agent, clock);
        using var subscription = harness.Feed.Subscribe();
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(2));
        agent.Metrics = Ok(rx: 100, tx: 100);
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        var readings = await TwoAsync(subscription);
        Assert.Equal(0d, readings[1].Network.ReceiveBytesPerSecond);
        Assert.Equal(0d, readings[1].Network.SendBytesPerSecond);
    }

    /// <summary>An agent that does not answer publishes nothing: a gap, not a reading of zeroes.</summary>
    [Fact]
    public async Task An_agent_that_does_not_answer_publishes_nothing_rather_than_a_reading_of_zeroes()
    {
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var clock = new FakeClock();
        using var harness = new Harness(agent, clock);

        Assert.False(await harness.Poller.PollOnceAsync(CancellationToken.None));

        var history = await harness.Feed.GetHistoryAsync(clock.UtcNow.AddHours(-1), CancellationToken.None);
        Assert.Empty(history);
    }

    /// <summary>After a gap the rate divides by the time since the last reading that WAS taken, so it is not a spike.</summary>
    [Fact]
    public async Task After_a_gap_the_rate_divides_by_the_time_since_the_last_reading_that_was_taken()
    {
        var agent = new StubAgentMonitorClient { Metrics = Ok(rx: 1_000, tx: 0) };
        var clock = new FakeClock();
        using var harness = new Harness(agent, clock);
        using var subscription = harness.Feed.Subscribe();
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(10));
        agent.Metrics = Result<AgentHostMetrics>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(10));
        agent.Metrics = Ok(rx: 3_000, tx: 0);
        await harness.Poller.PollOnceAsync(CancellationToken.None);

        var readings = await TwoAsync(subscription);
        Assert.Equal(100d, readings[1].Network.ReceiveBytesPerSecond);
    }

    /// <summary>The loop does not ask the agent while nobody is watching, and does once somebody is.</summary>
    [Fact]
    public async Task The_loop_does_not_ask_the_agent_while_nobody_is_watching_and_does_once_somebody_is()
    {
        var agent = new StubAgentMonitorClient();
        using var harness = new Harness(agent, new FakeClock());
        await harness.Poller.StartAsync(CancellationToken.None);

        // The loop is parked on the feed's first-subscriber wait; nothing can have asked the agent yet.
        Assert.Equal(0, agent.MetricsCalls);

        using var subscription = harness.Feed.Subscribe();
        var reading = await FirstAsync(subscription);

        Assert.True(agent.MetricsCalls >= 1);
        Assert.Equal(5d, reading.Cpu.TotalPercent);
        await harness.Poller.StopAsync(CancellationToken.None);
    }

    /// <summary>Builds a successful agent answer with the two counters a test cares about.</summary>
    /// <param name="rx">The received-bytes counter.</param>
    /// <param name="tx">The sent-bytes counter.</param>
    /// <param name="cpu">The processor utilisation.</param>
    /// <returns>The answer.</returns>
    private static Result<AgentHostMetrics> Ok(ulong rx, ulong tx, double cpu = 5)
    {
        return Result<AgentHostMetrics>.Ok(new AgentHostMetrics(cpu, 3_000, 8_000, 50, 5, 45, 100, rx, tx, 1.5, 1.2, 1.1, 1));
    }

    /// <summary>Reads the first reading a subscription delivers, bounded so a missing one fails rather than hangs.</summary>
    /// <param name="subscription">The subscription to read.</param>
    /// <returns>The first reading.</returns>
    private static async Task<LiveMetricsReading> FirstAsync(ILiveMetricsSubscription subscription)
    {
        return (await ReadAsync(subscription, 1))[0];
    }

    /// <summary>Reads the first two readings a subscription delivers.</summary>
    /// <param name="subscription">The subscription to read.</param>
    /// <returns>Two readings, oldest first.</returns>
    private static Task<List<LiveMetricsReading>> TwoAsync(ILiveMetricsSubscription subscription)
    {
        return ReadAsync(subscription, 2);
    }

    /// <summary>Reads <paramref name="count"/> readings from a subscription under the test deadline.</summary>
    /// <param name="subscription">The subscription to read.</param>
    /// <param name="count">How many readings to wait for.</param>
    /// <returns>The readings, oldest first.</returns>
    private static async Task<List<LiveMetricsReading>> ReadAsync(
        ILiveMetricsSubscription subscription,
        int count)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var readings = new List<LiveMetricsReading>();
        await foreach (var reading in subscription.ReadAllAsync(deadline.Token))
        {
            readings.Add(reading);
            if (readings.Count == count)
            {
                return readings;
            }
        }

        throw new InvalidOperationException("The subscription ended before delivering the readings.");
    }

    /// <summary>A poller and the feed it publishes to, built over an in-memory store.</summary>
    private sealed class Harness : IDisposable
    {
        /// <summary>The container behind the feed's scope factory.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>The store the feed's back-fill reads.</summary>
        private readonly MonitoringDbContext _dbContext;

        /// <summary>The feed the poller publishes to.</summary>
        public LiveMetricsFeed Feed { get; }

        /// <summary>The poller under test.</summary>
        public LiveMetricsPoller Poller { get; }

        /// <summary>Builds the pair.</summary>
        /// <param name="agent">The agent the poller asks.</param>
        /// <param name="clock">The clock both use.</param>
        public Harness(StubAgentMonitorClient agent, FakeClock clock)
        {
            _dbContext = MonitoringTestContext.Create();
            var services = new ServiceCollection();
            services.AddSingleton(_dbContext);
            _provider = services.BuildServiceProvider();

            var options = new OptionsWrapper<LiveMetricsOptions>(new LiveMetricsOptions());
            Feed = new LiveMetricsFeed(_provider.GetRequiredService<IServiceScopeFactory>(), clock, options);
            Poller = new LiveMetricsPoller(
                Feed,
                agent,
                clock,
                options,
                NullLogger<LiveMetricsPoller>.Instance);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Poller.Dispose();
            _provider.Dispose();
            _dbContext.Dispose();
        }
    }
}
