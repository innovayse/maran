using System.Globalization;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Tells the server's operator that the installed licence is close to its expiry, through the notifications
/// that already exist: the mail addresses, the alert webhook and the notification feed.
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns the words; the path is shared.</b> Journalling the alert, finding where operator alerts go and
/// publishing the mail, the webhook event and the feed entry are <see cref="OperatorAlertDispatcher"/>'s, the one
/// implementation every module calls. Nothing here makes a call of its own.
/// </para>
/// <para>
/// <b>An event, not a condition.</b> Each threshold crossed is one <see cref="AlertTransition.Occurred"/> alert,
/// like a certificate coming due, so the feed shows it as a warning rather than a critical outage and nothing has
/// to resolve it: installing a new licence simply produces no further alert.
/// </para>
/// <para>
/// <b>The words of the feed entry are the feed's own.</b> The detail is the webhook's fixed key=value line; the feed
/// sentence is composed by the Notifications module in the reader's language from the same two figures the alert
/// carries as its feed values (the day, the days left) and leads to the licence page.
/// </para>
/// <para>
/// <b>One day, one rule.</b> The day in the mail, the detail and the feed is the UTC calendar day of the expiry
/// instant, the same day the licence screen shows (it formats the instant in UTC), so they cannot disagree.
/// </para>
/// <para>
/// <b>The mail says when and how to renew, never which server.</b> It carries the expiry day and the days left, and
/// points at the licence screen, which has the claim link. It carries no licence id, no server id and no link, so
/// nothing that identifies the machine leaves the panel in a mail.
/// </para>
/// </remarks>
public sealed class LicenceExpiryAlerter
{
    /// <summary>The alert's code: the kind of alert, stable for receivers of the webhook and the feed.</summary>
    private const string AlertCode = "LicenceExpiring";

    /// <summary>The thing the alert is about.</summary>
    private const string AlertSubject = "licence";

    /// <summary>The audit subject of the alert's journal entry.</summary>
    private const string AuditSubject = "LicenceExpiry";

    /// <summary>How the expiry day is written in the mail and the detail: a UTC calendar day.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail, webhook, feed.</summary>
    private readonly OperatorAlertDispatcher _operatorAlerts;

    /// <summary>This module's audit journal.</summary>
    private readonly LicensingAuditJournal _journal;

    /// <summary>The subjects and bodies of the alert mail, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the alerter.</summary>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="text">The localized subject and body of the alert mail.</param>
    public LicenceExpiryAlerter(
        OperatorAlertDispatcher operatorAlerts,
        LicensingAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
    }

    /// <summary>Raises the alert: the licence has crossed an expiry threshold.</summary>
    /// <param name="warning">The threshold crossed and the time left.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onJournalled">
    /// Called once, right after the alert's journal row is stored and before the outputs are published. The caller
    /// remembers the alert as told there, so a failure of an output afterwards is not repeated as a second row.
    /// </param>
    /// <returns>Resolves once the alert is journalled and every output published or skipped.</returns>
    public async Task RaiseAsync(
        LicenceExpiryWarning warning,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? onJournalled = null)
    {
        var day = warning.Expiry.UtcDateTime.ToString(DayFormat, CultureInfo.InvariantCulture);
        var daysLeft = warning.DaysLeft.ToString(CultureInfo.InvariantCulture);

        var alert = new OperatorAlert(
            AlertSource.Licensing,
            AlertCode,
            AlertTransition.Occurred,
            AlertSubject,
            $"thresholdDays={warning.ThresholdDays.ToString(CultureInfo.InvariantCulture)} daysLeft={daysLeft} expiry={day}",
            [day, daysLeft]);

        ISystemAuditJournal journal = onJournalled is null ? _journal : new JournalledSignalJournal(_journal, onJournalled);

        await _operatorAlerts.AlertAsync(
            journal,
            MonitoringAuditActions.AlertRaised,
            AuditSubject,
            alert,
            _text["LicenceExpiryAlertSubject"].Value,
            _text["LicenceExpiryAlertBody", day, daysLeft].Value,
            cancellationToken);
    }
}
