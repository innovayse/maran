using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The evaluator over its real alert rows: ten breaching samples produce ONE request to send, and the
/// row is what makes that true.
/// </summary>
/// <remarks>
/// Everything here asserts on what was PUBLISHED, not on what was delivered. The evaluator no longer
/// sends: it asks the Notifications module to, exactly as Identity's password reset does. Whether the
/// mail server then accepted the message is that module's behaviour and is tested over there — which
/// is the whole point of the split, and is why none of these fixtures needs a mail server, a settings
/// row, or an <c>IMailer</c>.
/// </remarks>
public sealed class AlertEvaluatorTests
{
    /// <summary>The instant the first observation of every fixture is made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A disk above the threshold for ten consecutive samples sends one mail and journals one raise.</summary>
    /// <remarks>
    /// The deduplication guarantee end to end, through the row rather than through a field. Its
    /// mutation — dropping the <c>AlertStates</c> row, so nothing remembers the episode — sends one
    /// mail per sample from the tenth onward and turns this red.
    /// </remarks>
    [Fact]
    public async Task Ten_breaching_samples_send_one_alert_mail_and_journal_one_raise()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(95.0, [], Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Equal("ops@example.com", mail.Recipient);

        var raises = audit.Entries.Where(entry =>
        {
            return entry.Action == AuditActions.AlertRaised;
        }).ToList();
        Assert.Single(raises);
        Assert.Equal("DiskUsage:/", raises[0].Subject);
    }

    /// <summary>A disk that recovers sends the resolve mail once and journals one resolve.</summary>
    [Fact]
    public async Task A_disk_that_recovers_sends_one_resolve_mail()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(95.0, [], Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(40.0, [], Start.AddMinutes(30), CancellationToken.None);
        await evaluator.EvaluateAsync(40.0, [], Start.AddMinutes(31), CancellationToken.None);

        Assert.Equal(2, scopes.Bus.Published.Count);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertResolved;
        });
    }

    /// <summary>A disk exactly at the threshold is not an alarm.</summary>
    [Fact]
    public async Task A_disk_exactly_at_the_threshold_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(
                AlertEvaluator.DiskUsageThresholdPercent, [], Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
    }

    /// <summary>An unmeasurable filesystem is not observed at all, so it neither raises nor resolves.</summary>
    /// <remarks>
    /// A zero capacity is a filesystem the agent could not measure, not a full one. Treating it as a
    /// percentage would divide by zero and produce an infinity that beats every threshold — a disk
    /// emergency mailed about a disk the panel cannot see.
    /// </remarks>
    [Fact]
    public async Task A_filesystem_the_agent_could_not_measure_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>A service the agent cannot judge neither advances nor resets the alert.</summary>
    /// <remarks>
    /// On the Debian family the enabled SSH unit is a socket whose service is inactive from boot
    /// until the first connection. Reading that as stopped would mail about an outage on every such
    /// host at every reboot; reading it as running would silently close a real open episode.
    /// </remarks>
    [Fact]
    public async Task A_service_the_agent_cannot_judge_neither_advances_nor_resets_the_alert()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var unknown = new AgentServiceStatus(
            AgentManagedService.Ssh, AgentServiceState.Unknown, "ssh.socket is listening for it");

        for (var observation = 0; observation < 30; observation++)
        {
            await evaluator.EvaluateAsync(null, [unknown], Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>A stopped service raises once after ten consecutive reports, like the disk.</summary>
    [Fact]
    public async Task A_service_reported_stopped_for_ten_checks_raises_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var stopped = new AgentServiceStatus(
            AgentManagedService.WebServer, AgentServiceState.Stopped, "inactive (dead)");

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [stopped], Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Single(scopes.Bus.Published);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised && entry.Subject == "ServiceStopped:WebServer";
        });
    }

    /// <summary>An alert raised on a panel with no mail settings is journalled as raised AND as skipped.</summary>
    /// <remarks>
    /// The raise is a fact about the SERVER and must be recorded whatever happens to the mail; the
    /// skip is what explains, months later, why nobody was told. The state still transitions, so the
    /// panel does not re-raise on every following sample and then deliver a storm the moment mail is
    /// configured.
    /// </remarks>
    [Fact]
    public async Task An_alert_raised_with_no_mail_configured_is_journalled_as_raised_and_as_skipped()
    {
        await using var dbContext = MonitoringTestContext.Create();

        var recipients = new StubAlertRecipientDirectory();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(99.0, [], Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised;
        });
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.MailSkippedNoSmtp;
        });
    }
}
