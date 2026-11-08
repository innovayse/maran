using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.ValueObjects;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// How a scheduled panel update reaches the operator: the outcome is always journalled, a failure also
/// asks for a mail, and a success mails nothing.
/// </summary>
/// <remarks>
/// Asserted on what was journalled and PUBLISHED, like the alert evaluator's tests: whether the mail
/// server accepted the message is the Notifications module's behaviour.
/// </remarks>
public sealed class PanelUpdateReporterTests
{
    /// <summary>Builds a report that the value object accepts.</summary>
    /// <param name="outcome">How the run ended.</param>
    /// <returns>The report, for releases 1.2.3 and 1.2.4.</returns>
    private static PanelUpdateReport Report(PanelUpdateOutcome outcome)
    {
        Assert.True(PanelUpdateReport.TryCreate(outcome, "1.2.3", "1.2.4", out var report));
        return report;
    }

    /// <summary>A release that was applied is journalled as succeeded and no mail is requested.</summary>
    [Fact]
    public async Task An_applied_update_is_journalled_and_mails_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), audit);

        await scopes.Resolve<PanelUpdateReporter>().ReportAsync(Report(PanelUpdateOutcome.Applied), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(MonitoringAuditActions.PanelUpdateApplied, entry.Action);
        Assert.Equal("1.2.3->1.2.4", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Empty(scopes.Bus.Mail);
    }

    /// <summary>A failed update is journalled as failed, then raised as an operator alert and mailed once.</summary>
    [Theory]
    [InlineData(PanelUpdateOutcome.RolledBack, MonitoringAuditActions.PanelUpdateRolledBack)]
    [InlineData(PanelUpdateOutcome.RollbackFailed, MonitoringAuditActions.PanelUpdateRollbackFailed)]
    [InlineData(PanelUpdateOutcome.Interrupted, MonitoringAuditActions.PanelUpdateInterrupted)]
    public async Task A_failed_update_is_journalled_as_failed_and_mails_the_operator_once(
        PanelUpdateOutcome outcome,
        string expectedAction)
    {
        await using var dbContext = MonitoringTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory("ops@example.com"), audit);

        await scopes.Resolve<PanelUpdateReporter>().ReportAsync(Report(outcome), CancellationToken.None);

        Assert.Equal(2, audit.Entries.Count);
        Assert.Equal(expectedAction, audit.Entries[0].Action);
        Assert.False(audit.Entries[0].Succeeded);
        Assert.Equal(MonitoringAuditActions.AlertRaised, audit.Entries[1].Action);
        Assert.Equal($"PanelUpdate:{outcome}:1.2.3->1.2.4", audit.Entries[1].Subject);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(scopes.Bus.Mail));
        Assert.Equal("ops@example.com", mail.Recipient);
        Assert.StartsWith($"AlertPanelUpdate{outcome}Subject|1.2.3|1.2.4", mail.Subject, StringComparison.Ordinal);
        Assert.StartsWith($"AlertPanelUpdate{outcome}Body|1.2.3|1.2.4", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>With no alert address configured the failure is still journalled and the missing recipient is recorded.</summary>
    [Fact]
    public async Task A_failed_update_with_no_alert_address_is_journalled_and_the_skipped_mail_is_recorded()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(
            dbContext, new StubAgentMonitorClient(), new StubAlertRecipientDirectory(), audit);

        await scopes.Resolve<PanelUpdateReporter>().ReportAsync(Report(PanelUpdateOutcome.RolledBack), CancellationToken.None);

        Assert.Contains(audit.Entries, entry =>
        {
            return entry.Action == MonitoringAuditActions.PanelUpdateRolledBack && !entry.Succeeded;
        });
        Assert.Contains(audit.Entries, entry =>
        {
            return entry.Action == OutgoingMailAuditActions.MailSkippedNoSmtp;
        });
        Assert.Empty(scopes.Bus.Mail);
    }
}
