using System.Globalization;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Tells the administrator that an unattended backup did not happen the way its schedule promised:
/// a run that failed, a destination that refused it, a run that missed its hour (issue #134).
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns the words; the path is shared.</b> Only this module knows what the mail is about, so
/// the subject and body are composed here. Journalling the alert, finding where operator alerts go,
/// publishing <see cref="SendMailRequested"/> and journalling
/// <see cref="AuditActions.MailSkippedNoSmtp"/> when there is nobody to tell are
/// <see cref="OperatorAlertMailer"/>'s, the one implementation the Monitoring module's alert
/// evaluator calls too (issue #290). The entries are written through this module's own journal, so
/// they carry its actor. It holds no mailer and no SMTP settings.
/// </para>
/// <para>
/// <b>No deduplication, on purpose.</b> Every alert here is one event — one run, one refusal, one
/// missed hour — and the caller reaches each at most once: the sweep stamps a schedule as run before
/// it calls anything here, so the next tick cannot meet the same occurrence again.
/// </para>
/// </remarks>
public sealed class BackupAlertMailer
{
    /// <summary>Resource key of the subject of a failed scheduled run.</summary>
    private const string ScheduledRunFailedSubject = "AlertScheduledRunFailedSubject";

    /// <summary>Resource key of the body of a failed scheduled run.</summary>
    private const string ScheduledRunFailedBody = "AlertScheduledRunFailedBody";

    /// <summary>Resource key of the subject of a run whose destination refused it.</summary>
    private const string DestinationRefusedSubject = "AlertDestinationRefusedSubject";

    /// <summary>Resource key of the body of a run whose destination refused it.</summary>
    private const string DestinationRefusedBody = "AlertDestinationRefusedBody";

    /// <summary>Resource key of the subject of a run that missed its named hour.</summary>
    private const string ScheduledRunMissedSubject = "AlertScheduledRunMissedSubject";

    /// <summary>Resource key of the body of a run that missed its named hour.</summary>
    private const string ScheduledRunMissedBody = "AlertScheduledRunMissedBody";

    /// <summary>How an instant is written into a mail: ISO 8601 to the minute, always UTC.</summary>
    private const string InstantFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail.</summary>
    private readonly OperatorAlertMailer _operatorAlerts;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The subjects and bodies of the alert mail, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Names a failure code as an operator reads it.</summary>
    private readonly BackupFailureDisplayNames _failures;

    /// <summary>Creates the mailer.</summary>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    /// <param name="failures">Names a failure code as an operator reads it.</param>
    public BackupAlertMailer(
        OperatorAlertMailer operatorAlerts,
        BackupAuditJournal journal,
        IStringLocalizer<NotificationMessages> text,
        BackupFailureDisplayNames failures)
    {
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
        _failures = failures;
    }

    /// <summary>Reports a scheduled run that reached the agent and ended without an archive.</summary>
    /// <param name="accountUsername">The account the run was for.</param>
    /// <param name="failureCode">The code the failed row recorded.</param>
    /// <param name="cancellationToken">Cancels the journal writes.</param>
    /// <returns>A task that completes once the alert is journalled and the mail requested.</returns>
    public Task ScheduledRunFailedAsync(
        string accountUsername,
        string failureCode,
        CancellationToken cancellationToken)
    {
        return AlertAsync(
            BackupAlertKind.ScheduledRunFailed,
            accountUsername,
            _text[ScheduledRunFailedSubject, accountUsername].Value,
            _text[ScheduledRunFailedBody, accountUsername, _failures.Of(failureCode), failureCode].Value,
            cancellationToken);
    }

    /// <summary>Reports a scheduled run that was not started because its destination was refused.</summary>
    /// <param name="accountUsername">The account the run was for.</param>
    /// <param name="refusalCode">The code the destination resolver refused with.</param>
    /// <param name="cancellationToken">Cancels the journal writes.</param>
    /// <returns>A task that completes once the alert is journalled and the mail requested.</returns>
    public Task DestinationRefusedAsync(
        string accountUsername,
        string refusalCode,
        CancellationToken cancellationToken)
    {
        return AlertAsync(
            BackupAlertKind.DestinationRefused,
            accountUsername,
            _text[DestinationRefusedSubject, accountUsername].Value,
            _text[DestinationRefusedBody, accountUsername, _failures.Of(refusalCode), refusalCode].Value,
            cancellationToken);
    }

    /// <summary>Reports a schedule that started long enough after its named hour to have missed it.</summary>
    /// <param name="scheduleId">The schedule, which is the journal's subject.</param>
    /// <param name="accountUsernames">Every account the schedule covers, named in the body.</param>
    /// <param name="dueAt">The named hour that was missed.</param>
    /// <param name="startedAt">When the late run started.</param>
    /// <param name="cancellationToken">Cancels the journal writes.</param>
    /// <returns>A task that completes once the alert is journalled and the mail requested.</returns>
    public Task ScheduledRunMissedAsync(
        Guid scheduleId,
        IReadOnlyList<string> accountUsernames,
        DateTimeOffset dueAt,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        return AlertAsync(
            BackupAlertKind.ScheduledRunMissed,
            scheduleId.ToString(),
            _text[ScheduledRunMissedSubject].Value,
            _text[
                ScheduledRunMissedBody,
                FormatInstant(dueAt),
                FormatInstant(startedAt),
                string.Join(", ", accountUsernames)].Value,
            cancellationToken);
    }

    /// <summary>Writes an instant into a mail, in UTC and the invariant culture.</summary>
    /// <param name="instant">The instant to write.</param>
    /// <returns>The instant as <see cref="InstantFormat"/> in UTC.</returns>
    /// <remarks>
    /// Invariant because the figure sits inside a sentence that is itself localized, and a background
    /// sender has no request culture to read anyway — the reason the evaluator writes its figures so.
    /// </remarks>
    private static string FormatInstant(DateTimeOffset instant)
    {
        return instant.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Journals one alert and asks for its mail.</summary>
    /// <param name="kind">What the alert reports.</param>
    /// <param name="subject">What it is about — an account name or a schedule id.</param>
    /// <param name="mailSubject">The localized subject line.</param>
    /// <param name="mailBody">The localized body.</param>
    /// <param name="cancellationToken">Cancels the journal writes.</param>
    /// <returns>A task that completes once the alert is journalled and the mail requested.</returns>
    private Task AlertAsync(
        BackupAlertKind kind,
        string subject,
        string mailSubject,
        string mailBody,
        CancellationToken cancellationToken)
    {
        return _operatorAlerts.AlertAsync(
            _journal, AuditActions.AlertRaised, $"{kind}:{subject}", mailSubject, mailBody, cancellationToken);
    }
}
