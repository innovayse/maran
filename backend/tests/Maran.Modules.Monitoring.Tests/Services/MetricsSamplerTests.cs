using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>One sampling round: what it stores, and — the half that matters more — what it does not.</summary>
public sealed class MetricsSamplerTests
{
    /// <summary>A reading is stored with the panel's own clock and the agent's own figures.</summary>
    [Fact]
    public async Task A_reading_is_stored_with_the_panels_own_clock()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(
                new AgentHostMetrics(42.5, 3_000, 8_000, 50, 100, 111, 222, 1.5, 1.2, 1.1)),
        };
        var clock = new FakeClock();
        using var scopes = new TestScopeFactory(dbContext, agent, new StubAlertRecipientDirectory(), new RecordingAuditWriter());
        var sampler = Sampler(scopes, clock);

        Assert.True(await sampler.SampleOnceAsync(CancellationToken.None));

        var sample = Assert.Single(dbContext.Samples);
        Assert.Equal(clock.UtcNow, sample.CapturedAt);
        Assert.Equal(42.5, sample.CpuPercent);
        Assert.Equal(3_000, sample.MemoryUsedBytes);
        Assert.Equal(111, sample.NetworkRxBytes);
        Assert.Equal(222, sample.NetworkTxBytes);
        Assert.Equal(1.5, sample.LoadAverage1m);
    }

    /// <summary>An agent that does not answer leaves a gap rather than a row of zeroes.</summary>
    /// <remarks>
    /// A row of zeroes is a CLAIM about the machine — no memory in use, no traffic, no load — and
    /// every chart would draw it as one. A missing row is the truthful record of a minute the panel
    /// has no numbers for, which is why R7's rate arithmetic divides by measured time rather than by
    /// an assumed interval.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_does_not_answer_leaves_a_gap_rather_than_a_row_of_zeroes()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        using var scopes = new TestScopeFactory(dbContext, agent, new StubAlertRecipientDirectory(), new RecordingAuditWriter());
        var sampler = Sampler(scopes, new FakeClock());

        Assert.False(await sampler.SampleOnceAsync(CancellationToken.None));

        Assert.Empty(dbContext.Samples);
    }

    /// <summary>Statuses the agent could not list do not cost the round its sample.</summary>
    /// <remarks>
    /// The chart data is real whether or not the service manager answered, so a round that got the
    /// metrics still stores them; the evaluator is simply given no services to judge, which advances
    /// no alert counter in either direction.
    /// </remarks>
    [Fact]
    public async Task Statuses_the_agent_could_not_list_do_not_cost_the_round_its_sample()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var agent = new StubAgentMonitorClient
        {
            Statuses = Result<IReadOnlyList<AgentServiceStatus>>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        using var scopes = new TestScopeFactory(dbContext, agent, new StubAlertRecipientDirectory(), new RecordingAuditWriter());
        var sampler = Sampler(scopes, new FakeClock());

        Assert.True(await sampler.SampleOnceAsync(CancellationToken.None));

        Assert.Single(dbContext.Samples);

        // The disk WAS measured, so its alert row exists and is healthy; what must not exist is a row
        // for any service, because the agent named none.
        Assert.DoesNotContain(dbContext.AlertStates, state =>
        {
            return state.Kind == Monitoring.Domain.Enums.AlertKind.ServiceStopped;
        });
    }

    /// <summary>A byte count larger than the signed ceiling saturates rather than turning negative.</summary>
    /// <remarks>
    /// PostgreSQL has no unsigned integer type, so the column is a <c>bigint</c>. An unchecked cast of
    /// a value above <see cref="long.MaxValue"/> produces a NEGATIVE byte count, which draws a chart
    /// below zero and makes the rate arithmetic clamp an ordinary interval to nothing.
    /// </remarks>
    [Fact]
    public async Task A_byte_count_above_the_signed_ceiling_saturates_rather_than_turning_negative()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(
                new AgentHostMetrics(1, ulong.MaxValue, ulong.MaxValue, 1, 2, 3, 4, 0, 0, 0)),
        };
        using var scopes = new TestScopeFactory(dbContext, agent, new StubAlertRecipientDirectory(), new RecordingAuditWriter());
        var sampler = Sampler(scopes, new FakeClock());

        await sampler.SampleOnceAsync(CancellationToken.None);

        var sample = Assert.Single(dbContext.Samples);
        Assert.Equal(long.MaxValue, sample.MemoryUsedBytes);
        Assert.True(sample.MemoryUsedBytes > 0);
    }

    /// <summary>A full disk observed ten rounds running produces the alert, through the real round.</summary>
    /// <remarks>
    /// The evaluator's own tests drive it directly; this one proves the sampler actually calls it with
    /// the percentage it computed, which is the wiring a unit test of either half alone would miss.
    /// </remarks>
    [Fact]
    public async Task Ten_rounds_over_a_full_disk_raise_the_alert_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(
                new AgentHostMetrics(1, 10, 100, 990, 1_000, 0, 0, 0, 0, 0)),
        };
        var clock = new FakeClock();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, agent, new StubAlertRecipientDirectory(), audit);
        var sampler = Sampler(scopes, clock);

        for (var round = 0; round < 20; round++)
        {
            await sampler.SampleOnceAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(20, dbContext.Samples.Count());
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == Sdk.Contracts.AuditActions.AlertRaised;
        });
    }

    /// <summary>Builds the sampler over the container a test set up.</summary>
    /// <param name="scopes">The container each round opens a scope from.</param>
    /// <param name="clock">The clock every sample is stamped with.</param>
    /// <returns>The sampler.</returns>
    private static MetricsSampler Sampler(TestScopeFactory scopes, FakeClock clock)
    {
        return new MetricsSampler(scopes.Scopes, clock, NullLogger<MetricsSampler>.Instance);
    }
}
