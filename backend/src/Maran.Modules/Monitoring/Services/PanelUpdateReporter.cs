using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.ValueObjects;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using SdkAlertTransition = Maran.Sdk.Contracts.AlertTransition;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Records how a scheduled, unattended panel update ended and, when it failed, tells the operator
/// (issue #383).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the panel is told at all.</b> The update runs from a systemd timer as root, long after
/// anyone looked at a terminal. Its own journal line and a failed unit are the record that always
/// exists, but a server whose operator never logs in is exactly the one the feature is for, so the
/// outcome also goes where the operator already looks: the audit journal, and a mail to the address
/// monitoring alerts use.
/// </para>
/// <para>
/// <b>The outcome is journalled as what it was.</b> A failure is written with
/// <c>succeeded: false</c>, because the update did not take effect; the alert that follows is
/// journalled by <see cref="OperatorAlertDispatcher"/> as <see cref="MonitoringAuditActions.AlertRaised"/>,
/// exactly as every other operator alert is, so an administrator reading the journal finds the
/// failure and the fact that the operator was told. A success is journalled and mails nothing: a
/// patch that applied is a record, not an emergency.
/// </para>
/// <para>
/// <b>What the entry and the mail carry.</b> The outcome and two release names that
/// <see cref="PanelUpdateReport"/> already refused to hold unless they were plain. No dump path, no
/// URL, no tool output and no error text: the report arrives from a root process that has just been
/// through a failure, and none of that belongs in a mail (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class PanelUpdateReporter
{
    /// <summary>The operator-alert subject prefix, so the journal's alert entries name the kind of alert.</summary>
    private const string AlertSubjectPrefix = "PanelUpdate";

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail.</summary>
    private readonly OperatorAlertDispatcher _operatorAlerts;

    /// <summary>This module's audit journal.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>The subjects and bodies of the alert mail, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the reporter.</summary>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    public PanelUpdateReporter(
        OperatorAlertDispatcher operatorAlerts,
        MonitoringAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
    }

    /// <summary>Journals the outcome and, for a failure, asks for the operator's mail.</summary>
    /// <param name="report">How the run ended and which releases it concerned.</param>
    /// <param name="cancellationToken">Cancels the journal writes and the recipient read.</param>
    /// <returns>A task that completes once the outcome is journalled and the mail requested or skipped.</returns>
    public async Task ReportAsync(PanelUpdateReport report, CancellationToken cancellationToken)
    {
        var subject = $"{report.From}->{report.To}";
        var action = ActionFor(report.Outcome);

        if (report.Outcome == PanelUpdateOutcome.Applied)
        {
            await _journal.RecordSystemAsync(action, subject, succeeded: true, cancellationToken);
            return;
        }

        await _journal.RecordSystemAsync(action, subject, succeeded: false, cancellationToken);

        var key = report.Outcome.ToString();
        await _operatorAlerts.AlertAsync(
            _journal,
            MonitoringAuditActions.AlertRaised,
            $"{AlertSubjectPrefix}:{key}:{subject}",
            new OperatorAlert(AlertSource.Monitoring, $"{AlertSubjectPrefix}{key}", SdkAlertTransition.Raised, subject, string.Empty),
            _text[$"Alert{AlertSubjectPrefix}{key}Subject", report.From, report.To].Value,
            _text[$"Alert{AlertSubjectPrefix}{key}Body", report.From, report.To].Value,
            cancellationToken);
    }

    /// <summary>The audit action that records an outcome.</summary>
    /// <param name="outcome">How the run ended.</param>
    /// <returns>One of the Sdk's <see cref="MonitoringAuditActions"/> constants.</returns>
    private static string ActionFor(PanelUpdateOutcome outcome)
    {
        return outcome switch
        {
            PanelUpdateOutcome.Applied => MonitoringAuditActions.PanelUpdateApplied,
            PanelUpdateOutcome.RolledBack => MonitoringAuditActions.PanelUpdateRolledBack,
            PanelUpdateOutcome.RollbackFailed => MonitoringAuditActions.PanelUpdateRollbackFailed,
            PanelUpdateOutcome.Interrupted => MonitoringAuditActions.PanelUpdateInterrupted,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A report holds a defined outcome."),
        };
    }
}
