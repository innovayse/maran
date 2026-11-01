using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The free-space floor's warning (issue #148): raised before the agent's refusals begin, once, and
/// resolved when the room comes back.
/// </summary>
public sealed class AlertEvaluatorFreeSpaceFloorTests
{
    /// <summary>The instant the first observation of every fixture is made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One gibibyte, the installer's default floor.</summary>
    private const ulong Floor = 1024UL * 1024 * 1024;

    /// <summary>
    /// A root filesystem inside the margin — above the floor, so the agent is not refusing yet — raises
    /// one mail after the usual run of breaching checks, and the mail names the space left.
    /// </summary>
    [Fact]
    public async Task Room_under_twice_the_floor_raises_one_warning_before_the_floor_is_reached()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), new RecordingAuditWriter());
        var evaluator = scopes.Resolve<AlertEvaluator>();
        var reading = new FreeSpaceFloorReading(Floor + (Floor / 2), Floor);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert * 2; observation++)
        {
            await evaluator.EvaluateAsync(
                null, reading, [], null, null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Contains("1.5", mail.Body);
        var row = await dbContext.AlertStates.SingleAsync();
        Assert.Equal(AlertKind.FreeSpaceFloorApproaching, row.Kind);
        Assert.True(row.IsFiring);
    }

    /// <summary>Exactly twice the floor never raises: the inverse control.</summary>
    [Fact]
    public async Task Room_of_exactly_twice_the_floor_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), new RecordingAuditWriter());
        var evaluator = scopes.Resolve<AlertEvaluator>();
        var reading = new FreeSpaceFloorReading(2 * Floor, Floor);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert * 2; observation++)
        {
            await evaluator.EvaluateAsync(
                null, reading, [], null, null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
    }

    /// <summary>A raised warning resolves, with one more mail, once the room is back above the margin.</summary>
    [Fact]
    public async Task A_raised_warning_resolves_when_the_room_comes_back()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), new RecordingAuditWriter());
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(
                null, new FreeSpaceFloorReading(Floor, Floor), [], null, null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(
            null, new FreeSpaceFloorReading(4 * Floor, Floor), [], null, null, null, null, Start.AddMinutes(30), CancellationToken.None);

        Assert.Equal(2, scopes.Bus.Published.Count);
        Assert.False((await dbContext.AlertStates.SingleAsync()).IsFiring);
    }

    /// <summary>A round that did not read the floor observes nothing at all.</summary>
    [Fact]
    public async Task A_round_without_a_reading_observes_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), new RecordingAuditWriter());
        var evaluator = scopes.Resolve<AlertEvaluator>();

        await evaluator.EvaluateAsync(null, null, [], null, null, null, null, Start, CancellationToken.None);

        Assert.Empty(dbContext.AlertStates);
    }
}
