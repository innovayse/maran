using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The clock-synchronisation warning (issue #150): raised once after the usual run of breaching
/// checks, through the shared operator-alert path, and resolved when the clock is synchronised again.
/// </summary>
public sealed class AlertEvaluatorClockTests
{
    /// <summary>The instant the first observation of every fixture is made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Each unsynchronised state raises exactly one mail, and the row names the clock.</summary>
    /// <param name="state">The state the agent keeps reporting.</param>
    [Theory]
    [InlineData(AgentClockSyncState.NotSynchronized)]
    [InlineData(AgentClockSyncState.SyncServiceDisabled)]
    public async Task An_unsynchronised_clock_raises_one_warning(AgentClockSyncState state)
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert * 2; observation++)
        {
            await EvaluateAsync(evaluator, state, Start.AddMinutes(observation));
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Equal("ops@example.com", mail.Recipient);
        Assert.StartsWith("AlertClockUnsynchronizedRaisedSubject", mail.Subject, StringComparison.Ordinal);
        var row = await dbContext.AlertStates.SingleAsync();
        Assert.Equal(AlertKind.ClockUnsynchronized, row.Kind);
        Assert.Equal(AlertEvaluator.SystemClockSubject, row.Subject);
        Assert.True(row.IsFiring);
    }

    /// <summary>A synchronised clock never raises: the inverse control.</summary>
    [Fact]
    public async Task A_synchronised_clock_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert * 2; observation++)
        {
            await EvaluateAsync(evaluator, AgentClockSyncState.Synchronized, Start.AddMinutes(observation));
        }

        Assert.Empty(scopes.Bus.Published);
    }

    /// <summary>A raised warning resolves, with one more mail, once the clock is synchronised.</summary>
    [Fact]
    public async Task A_raised_warning_resolves_when_the_clock_is_synchronised()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await EvaluateAsync(evaluator, AgentClockSyncState.NotSynchronized, Start.AddMinutes(observation));
        }

        await EvaluateAsync(evaluator, AgentClockSyncState.Synchronized, Start.AddMinutes(30));

        Assert.Equal(2, scopes.Bus.Published.Count);
        Assert.False((await dbContext.AlertStates.SingleAsync()).IsFiring);
    }

    /// <summary>A round in which the clock could not be read observes nothing at all.</summary>
    [Fact]
    public async Task A_round_without_a_reading_observes_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        await EvaluateAsync(evaluator, null, Start);

        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>Builds the scope factory every case resolves the evaluator from.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <returns>The factory.</returns>
    private static TestScopeFactory NewScopes(MonitoringDbContext dbContext)
    {
        return new TestScopeFactory(
            dbContext,
            new StubAgentMonitorClient(),
            new StubAlertRecipientDirectory("ops@example.com"),
            new RecordingAuditWriter());
    }

    /// <summary>Evaluates a round that carries only a clock reading.</summary>
    /// <param name="evaluator">The evaluator under test.</param>
    /// <param name="clockSync">The clock reading, or <c>null</c> for an unanswered one.</param>
    /// <param name="observedAt">When the round was taken.</param>
    /// <returns>A task that completes when the round is evaluated.</returns>
    private static async Task EvaluateAsync(
        AlertEvaluator evaluator,
        AgentClockSyncState? clockSync,
        DateTimeOffset observedAt)
    {
        await evaluator.EvaluateAsync(
            diskUsedPercent: null,
            freeSpaceFloor: null,
            services: [],
            sftpJailStatus: null,
            quotaStatus: null,
            clockSync: clockSync,
            codeIntegrityReport: null,
            observedAt,
            CancellationToken.None);
    }
}
