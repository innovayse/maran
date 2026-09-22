using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

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
            await evaluator.EvaluateAsync(95.0, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
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
            await evaluator.EvaluateAsync(95.0, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(40.0, [], null, null, null, Start.AddMinutes(30), CancellationToken.None);
        await evaluator.EvaluateAsync(40.0, [], null, null, null, Start.AddMinutes(31), CancellationToken.None);

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
                AlertEvaluator.DiskUsageThresholdPercent, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
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
            await evaluator.EvaluateAsync(null, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
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
            await evaluator.EvaluateAsync(null, [unknown], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
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
            await evaluator.EvaluateAsync(null, [stopped], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Single(scopes.Bus.Published);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised && entry.Subject == "ServiceStopped:WebServer";
        });
    }

    /// <summary>
    /// A drifted SFTP jail block raises once after ten consecutive checks, exactly like the disk and
    /// a stopped service — the exact defect release-readiness issue #28 item E names, now observed.
    /// </summary>
    [Fact]
    public async Task A_drifted_sftp_jail_reported_for_ten_checks_raises_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var drifted = new AgentSftpJailStatus(true, ["forcecommand internal-sftp"]);

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], drifted, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Equal("ops@example.com", mail.Recipient);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised
                && entry.Subject == $"SftpJailDrifted:{AlertEvaluator.SftpJailSubject}";
        });
    }

    /// <summary>A jail block restored after having drifted sends the resolve mail once.</summary>
    [Fact]
    public async Task A_restored_sftp_jail_sends_one_resolve_mail()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var drifted = new AgentSftpJailStatus(true, ["forcecommand internal-sftp"]);
        var intact = new AgentSftpJailStatus(false, []);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], drifted, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(null, [], intact, null, null, Start.AddMinutes(30), CancellationToken.None);
        await evaluator.EvaluateAsync(null, [], intact, null, null, Start.AddMinutes(31), CancellationToken.None);

        Assert.Equal(2, scopes.Bus.Published.Count);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertResolved
                && entry.Subject == $"SftpJailDrifted:{AlertEvaluator.SftpJailSubject}";
        });
    }

    /// <summary>
    /// A failed call to the agent is not evidence about the jail — it must neither advance nor reset
    /// the alert, for the same reason an unmeasurable filesystem does not.
    /// </summary>
    [Fact]
    public async Task A_failed_sftp_jail_check_raises_nothing_and_creates_no_row()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.Empty(dbContext.AlertStates);
    }

    /// <summary>
    /// A filesystem reported unable to enforce quotas for ten consecutive checks raises once, exactly
    /// like the disk, a stopped service, and the SFTP jail — a remount can flip this without touching
    /// any account, which is exactly why it is observed continuously rather than only at install time.
    /// </summary>
    [Fact]
    public async Task A_filesystem_unable_to_enforce_quotas_for_ten_checks_raises_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var unenforceable = new AgentQuotaEnforceability(false, QuotaUnenforceableReason.AccountingNotEnabled);

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, unenforceable, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Equal("ops@example.com", mail.Recipient);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised
                && entry.Subject == $"QuotaNotEnforceable:{AlertEvaluator.AccountHomeFilesystemSubject}";
        });
    }

    /// <summary>A filesystem whose quota enforceability is restored sends the resolve mail once.</summary>
    [Fact]
    public async Task A_filesystem_whose_quota_enforceability_is_restored_sends_one_resolve_mail()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var unenforceable = new AgentQuotaEnforceability(false, QuotaUnenforceableReason.AccountingNotEnabled);
        var enforceable = new AgentQuotaEnforceability(true, QuotaUnenforceableReason.Unspecified);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, unenforceable, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(null, [], null, enforceable, null, Start.AddMinutes(30), CancellationToken.None);
        await evaluator.EvaluateAsync(null, [], null, enforceable, null, Start.AddMinutes(31), CancellationToken.None);

        Assert.Equal(2, scopes.Bus.Published.Count);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertResolved
                && entry.Subject == $"QuotaNotEnforceable:{AlertEvaluator.AccountHomeFilesystemSubject}";
        });
    }

    /// <summary>
    /// A failed call to the agent is not evidence about quota enforceability — it must neither
    /// advance nor reset the alert, for the same reason a failed SFTP jail check does not.
    /// </summary>
    [Fact]
    public async Task A_failed_quota_enforceability_check_raises_nothing_and_creates_no_row()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.Empty(dbContext.AlertStates);
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
            await evaluator.EvaluateAsync(99.0, [], null, null, null, Start.AddMinutes(observation), CancellationToken.None);
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

    /// <summary>A drifted code-integrity report raises the installed-files alert, naming what differs.</summary>
    /// <remarks>
    /// A report that cannot name what differs would provide no more information than a boolean and
    /// defeat the entire feature's purpose (docs/superpowers/plans/2026-09-19-maran-code-integrity.md
    /// Task 3's inverse control) — this asserts the journal's subject actually carries the version,
    /// the count, and the paths, not a generic sentence.
    /// </remarks>
    [Fact]
    public async Task A_drifted_code_integrity_report_raises_and_names_the_differing_paths()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var report = new CodeIntegrityReport(
            CodeIntegrityOutcome.Drifted,
            "1.4.2",
            ["api/Maran.Host", "agent/maran-agent", "frontend/index.html"],
            null,
            Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, report, Start.AddMinutes(observation), CancellationToken.None);
        }

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Contains("api/Maran.Host", mail.Body);
        Assert.Contains("agent/maran-agent", mail.Body);
        Assert.Contains("frontend/index.html", mail.Body);

        var raised = Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised;
        });
        Assert.Contains("version=1.4.2", raised.Subject);
        Assert.Contains("count=3", raised.Subject);
        Assert.Contains("api/Maran.Host", raised.Subject);
    }

    /// <summary>An unmodified install sends no mail and journals no raise.</summary>
    [Fact]
    public async Task A_clean_code_integrity_report_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Clean, "1.4.2", [], null, Start);

        for (var observation = 0; observation < 20; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, report, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Published);
        Assert.DoesNotContain(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.AlertRaised;
        });
    }

    /// <summary>
    /// A Clean report resolves BOTH the installed-files alert and a hash-list-availability alert that
    /// was left open, so an availability alert with nothing to do with drift is not stuck open forever
    /// once drift also clears.
    /// </summary>
    [Fact]
    public async Task A_clean_report_resolves_both_the_installed_files_and_hash_list_alerts()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var unavailable = new CodeIntegrityReport(
            CodeIntegrityOutcome.Unavailable, "1.4.2", [], "hash list missing", Start);
        var drifted = new CodeIntegrityReport(
            CodeIntegrityOutcome.Drifted, "1.4.2", ["api/Maran.Host"], null, Start);
        var clean = new CodeIntegrityReport(CodeIntegrityOutcome.Clean, "1.4.2", [], null, Start);

        // Raise the hash-list alert first (unavailable), then raise the installed-files alert
        // (drifted) on top of it, in separate rounds — mirroring two independent episodes that can be
        // open at once.
        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, unavailable, Start.AddMinutes(observation), CancellationToken.None);
        }

        for (var observation = 10; observation < 10 + AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, drifted, Start.AddMinutes(observation), CancellationToken.None);
        }

        await evaluator.EvaluateAsync(null, [], null, null, clean, Start.AddMinutes(30), CancellationToken.None);

        var installedFiles = await dbContext.AlertStates.SingleAsync(row => row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.InstalledFilesSubject);
        var hashList = await dbContext.AlertStates.SingleAsync(row => row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.HashListSubject);

        Assert.False(installedFiles.IsFiring);
        Assert.False(hashList.IsFiring);
    }

    /// <summary>An unanswered round (no report at all) neither raises nor resolves an open code-integrity episode.</summary>
    /// <remarks>
    /// The same "unanswered raises AND resolves nothing" symmetry the existing SFTP-jail null case
    /// already satisfies, restated for this input rather than assumed to transfer
    /// (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 3's second mutant).
    /// </remarks>
    [Fact]
    public async Task A_null_code_integrity_report_leaves_an_open_episode_untouched()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var drifted = new CodeIntegrityReport(
            CodeIntegrityOutcome.Drifted, "1.4.2", ["api/Maran.Host"], null, Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, drifted, Start.AddMinutes(observation), CancellationToken.None);
        }

        var beforeUnanswered = await dbContext.AlertStates.SingleAsync(row => row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.InstalledFilesSubject);
        Assert.True(beforeUnanswered.IsFiring);
        var consecutiveBefore = beforeUnanswered.ConsecutiveBreaches;
        var publishedBefore = scopes.Bus.Published.Count;

        await evaluator.EvaluateAsync(null, [], null, null, null, Start.AddMinutes(30), CancellationToken.None);

        var afterUnanswered = await dbContext.AlertStates.SingleAsync(row => row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.InstalledFilesSubject);
        Assert.True(afterUnanswered.IsFiring);
        Assert.Equal(consecutiveBefore, afterUnanswered.ConsecutiveBreaches);
        Assert.Equal(publishedBefore, scopes.Bus.Published.Count);
    }

    /// <summary>
    /// A Drifted report with an empty differing-paths list is a contract violation the handler must
    /// not interpret charitably as Clean — it is rejected and journalled, and it neither raises nor
    /// resolves either code-integrity subject.
    /// </summary>
    [Fact]
    public async Task A_drifted_report_with_no_differing_paths_is_rejected_not_treated_as_clean()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var malformed = new CodeIntegrityReport(CodeIntegrityOutcome.Drifted, "1.4.2", [], null, Start);

        await evaluator.EvaluateAsync(null, [], null, null, malformed, Start, CancellationToken.None);

        Assert.Empty(scopes.Bus.Published);
        Assert.Empty(dbContext.AlertStates);
        Assert.Single(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.CodeIntegrityReportRejected;
        });
    }

    /// <summary>An unavailable comparison raises the hash-list alert with the closed side's own reason, never the installed-files alert.</summary>
    [Fact]
    public async Task An_unavailable_report_raises_only_the_hash_list_alert()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var recipients = new StubAlertRecipientDirectory("ops@example.com");
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext, new StubAgentMonitorClient(), recipients, audit);
        var evaluator = scopes.Resolve<AlertEvaluator>();

        var unavailable = new CodeIntegrityReport(
            CodeIntegrityOutcome.Unavailable, "1.4.2", [], "signature verification failed", Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            await evaluator.EvaluateAsync(null, [], null, null, unavailable, Start.AddMinutes(observation), CancellationToken.None);
        }

        Assert.DoesNotContain(dbContext.AlertStates.ToList(), row =>
        {
            return row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.InstalledFilesSubject;
        });

        var hashList = await dbContext.AlertStates.SingleAsync(row => row.Kind == AlertKind.CodeIntegrityDrifted && row.Subject == AlertEvaluator.HashListSubject);
        Assert.True(hashList.IsFiring);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Published));
        Assert.Contains("signature verification failed", mail.Body);
    }
}
