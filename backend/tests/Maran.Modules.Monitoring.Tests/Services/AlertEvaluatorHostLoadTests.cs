using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The evaluator over the administrator's thresholds: the processor and the memory alarm only while a
/// threshold is set, and the disk alarms at the figure saved rather than at a constant (issue #142).
/// </summary>
public sealed class AlertEvaluatorHostLoadTests
{
    /// <summary>The instant the first observation of every fixture is made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>With no thresholds saved a saturated processor and memory raise nothing and leave no row.</summary>
    /// <remarks>
    /// The default of an upgrade: the processor and memory were never watched, so they stay unwatched
    /// until an administrator says otherwise. The control is the next test, with a threshold set.
    /// </remarks>
    [Fact]
    public async Task With_no_thresholds_saved_a_saturated_host_raises_nothing_and_leaves_no_row()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(100.0, 100.0), Start.AddMinutes(observation));
        }

        Assert.Empty(scopes.Bus.Mail);
        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>Ten consecutive samples above the processor threshold send one mail and journal one raise.</summary>
    [Fact]
    public async Task Ten_samples_above_the_processor_threshold_send_one_mail_and_journal_one_raise()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        var audit = new RecordingAuditWriter();
        using var scopes = NewScopes(dbContext, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(observation));
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Mail));
        Assert.StartsWith("AlertCpuUsageRaisedSubject", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("95.0", mail.Body, StringComparison.Ordinal);
        var raise = Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == MonitoringAuditActions.AlertRaised;
        });
        Assert.Equal("CpuUsage:cpu", raise.Subject);
        var row = await dbContext.AlertStates.SingleAsync();
        Assert.Equal(AlertKind.CpuUsage, row.Kind);
        Assert.Equal(AlertPhase.Firing, row.Phase);
    }

    /// <summary>A spike shorter than ten samples is not an alert.</summary>
    [Fact]
    public async Task A_spike_shorter_than_ten_samples_is_not_an_alert()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert - 1; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(99.0, 10.0), Start.AddMinutes(observation));
        }

        await EvaluateAsync(evaluator, new HostLoadReading(10.0, 10.0), Start.AddMinutes(30));

        Assert.Empty(scopes.Bus.Mail);
    }

    /// <summary>A processor exactly at its threshold is not an alarm: the inverse control of the raise.</summary>
    [Fact]
    public async Task A_processor_exactly_at_its_threshold_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(80.0, 10.0), Start.AddMinutes(observation));
        }

        Assert.Empty(scopes.Bus.Mail);
    }

    /// <summary>Memory above its threshold raises its own kind and mail, independent of the processor.</summary>
    [Fact]
    public async Task Memory_above_its_threshold_raises_its_own_kind_independent_of_the_processor()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, null, 75);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(100.0, 80.0), Start.AddMinutes(observation));
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Mail));
        Assert.StartsWith("AlertMemoryUsageRaisedSubject", mail.Subject, StringComparison.Ordinal);
        var row = await dbContext.AlertStates.SingleAsync();
        Assert.Equal(AlertKind.MemoryUsage, row.Kind);
    }

    /// <summary>Recovering below the threshold sends one resolve mail.</summary>
    [Fact]
    public async Task Recovering_below_the_threshold_sends_one_resolve_mail()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(observation));
        }

        await EvaluateAsync(evaluator, new HostLoadReading(20.0, 10.0), Start.AddMinutes(30));
        await EvaluateAsync(evaluator, new HostLoadReading(20.0, 10.0), Start.AddMinutes(31));

        Assert.Equal(2, scopes.Bus.Mail.Count);
        var resolve = Assert.IsType<SendMailRequested>(scopes.Bus.Mail[1]);
        Assert.StartsWith("AlertCpuUsageResolvedSubject", resolve.Subject, StringComparison.Ordinal);
    }

    /// <summary>Switching the threshold off while an episode is firing resolves it once and keeps it resolved.</summary>
    [Fact]
    public async Task Switching_the_threshold_off_while_firing_resolves_the_episode_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        var audit = new RecordingAuditWriter();
        using var scopes = NewScopes(dbContext, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(observation));
        }

        await SaveThresholdsAsync(dbContext, 90, null, null);

        await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(30));
        await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(31));

        Assert.Equal(2, scopes.Bus.Mail.Count);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == MonitoringAuditActions.AlertResolved;
        });
        Assert.NotEqual(AlertPhase.Firing, (await dbContext.AlertStates.SingleAsync()).Phase);
    }

    /// <summary>A round with no host-load reading advances nothing and resolves nothing.</summary>
    [Fact]
    public async Task A_round_without_a_host_load_reading_advances_nothing_and_resolves_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, 80, null);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(95.0, 10.0), Start.AddMinutes(observation));
        }

        for (var observation = 20; observation < 40; observation++)
        {
            await EvaluateAsync(evaluator, null, Start.AddMinutes(observation));
        }

        Assert.Single(scopes.Bus.Mail);
        Assert.Equal(AlertPhase.Firing, (await dbContext.AlertStates.SingleAsync()).Phase);
    }

    /// <summary>Memory the agent could not measure is not observed, even with a threshold set.</summary>
    [Fact]
    public async Task Memory_the_agent_could_not_measure_is_not_observed()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 90, null, 75);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await EvaluateAsync(evaluator, new HostLoadReading(100.0, null), Start.AddMinutes(observation));
        }

        Assert.Empty(scopes.Bus.Mail);
        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>The disk alarms above the saved figure, not above ninety.</summary>
    [Fact]
    public async Task The_disk_alarms_above_the_saved_figure_not_above_ninety()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SaveThresholdsAsync(dbContext, 70, null, null);
        using var scopes = NewScopes(dbContext);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(
                75.0, null, [], null, null, null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Mail));
        Assert.StartsWith("AlertDiskUsageRaisedSubject", mail.Subject, StringComparison.Ordinal);
    }

    /// <summary>Stores thresholds the way a save does, replacing whatever is there.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="disk">The disk threshold.</param>
    /// <param name="cpu">The processor threshold, or <c>null</c> for unwatched.</param>
    /// <param name="memory">The memory threshold, or <c>null</c> for unwatched.</param>
    /// <returns>A task that completes once the row is saved.</returns>
    private static async Task SaveThresholdsAsync(MonitoringDbContext dbContext, int disk, int? cpu, int? memory)
    {
        var row = await dbContext.AlertThresholds.FirstOrDefaultAsync(thresholds => thresholds.Id == AlertThresholds.SingletonId);

        if (row is null)
        {
            row = AlertThresholds.CreateDefault(Start);
            dbContext.AlertThresholds.Add(row);
        }

        row.Replace(disk, cpu, memory, Start);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Builds the scope factory every case resolves the evaluator from.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="audit">The journal double; a fresh one when omitted.</param>
    /// <returns>The factory.</returns>
    private static TestScopeFactory NewScopes(MonitoringDbContext dbContext, RecordingAuditWriter? audit = null)
    {
        return new TestScopeFactory(
            dbContext,
            new StubAgentMonitorClient(),
            new StubAlertRecipientDirectory("ops@example.com"),
            audit ?? new RecordingAuditWriter());
    }

    /// <summary>Runs one round that carries only a host-load reading.</summary>
    /// <param name="evaluator">The evaluator under test.</param>
    /// <param name="hostLoad">The reading, or <c>null</c> for an unanswered round.</param>
    /// <param name="observedAt">When the round was taken.</param>
    /// <returns>A task that completes when the round is evaluated.</returns>
    private static async Task EvaluateAsync(AlertEvaluator evaluator, HostLoadReading? hostLoad, DateTimeOffset observedAt)
    {
        await evaluator.EvaluateAsync(
            diskUsedPercent: null,
            freeSpaceFloor: null,
            services: [],
            sftpJailStatus: null,
            quotaStatus: null,
            clockSync: null,
            codeIntegrityVerdict: null,
            hostLoad: hostLoad,
            observedAt,
            CancellationToken.None);
    }
}
