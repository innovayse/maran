using System.Globalization;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Tells the server's operator that the licence lease keeps failing to renew, and later that it has stopped
/// failing, so a lapse of the paid modules is seen coming while the current lease still has most of its life
/// left (decision: an alert on repeated failure, through the notifications that already exist).
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns the words; the path is shared.</b> Journalling the alert, finding where operator alerts go and
/// publishing the mail, the webhook event and the feed entry are <see cref="OperatorAlertDispatcher"/>'s, the
/// one implementation Monitoring, Backups and Ssl call too. The entries are written through this module's own
/// journal (<see cref="LicensingAuditJournal"/>).
/// </para>
/// <para>
/// <b>When it is called is not decided here</b> but by <see cref="Domain.Policies.LeaseRenewalPolicy"/> and
/// the worker that applies it: once when the run of failures reaches the threshold, once when a renewal then
/// succeeds.
/// </para>
/// <para>
/// <b>The mail says what and how often, never where or how.</b> It names the count and one closed reason
/// sentence. It carries no licence id, no address, no response text and no path (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class LeaseRenewalAlerter
{
    /// <summary>The alert's code: the kind of alert, stable for receivers of the webhook and the feed.</summary>
    private const string AlertCode = "LeaseRenewalFailed";

    /// <summary>The thing the alert is about.</summary>
    private const string AlertSubject = "licence lease";

    /// <summary>The audit subject of the alert's journal entries.</summary>
    private const string AuditSubject = "LeaseRenewal";

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail, webhook, feed.</summary>
    private readonly OperatorAlertDispatcher _operatorAlerts;

    /// <summary>This module's audit journal.</summary>
    private readonly LicensingAuditJournal _journal;

    /// <summary>The subjects and bodies of the alert mail, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the alerter.</summary>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    public LeaseRenewalAlerter(
        OperatorAlertDispatcher operatorAlerts,
        LicensingAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
    }

    /// <summary>Raises the alert: renewals have failed repeatedly.</summary>
    /// <param name="outcome">What the latest failed pass amounted to.</param>
    /// <param name="consecutiveFailures">The length of the run of failed passes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once the alert is journalled and every output published or skipped.</returns>
    public async Task RaiseAsync(
        LeaseRenewalOutcome outcome,
        int consecutiveFailures,
        CancellationToken cancellationToken)
    {
        var count = consecutiveFailures.ToString(CultureInfo.InvariantCulture);
        var reason = _text["LeaseRenewalReason" + outcome].Value;

        var alert = new OperatorAlert(
            AlertSource.Licensing,
            AlertCode,
            AlertTransition.Raised,
            AlertSubject,
            $"outcome={outcome} consecutiveFailures={count}");

        await _operatorAlerts.AlertAsync(
            _journal,
            MonitoringAuditActions.AlertRaised,
            AuditSubject,
            alert,
            _text["LeaseRenewalFailedAlertSubject"].Value,
            _text["LeaseRenewalFailedAlertBody", count, reason].Value,
            cancellationToken);
    }

    /// <summary>Resolves the alert: a renewal succeeded after it was raised.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once the alert is journalled and every output published or skipped.</returns>
    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        var alert = new OperatorAlert(
            AlertSource.Licensing,
            AlertCode,
            AlertTransition.Resolved,
            AlertSubject,
            "outcome=Renewed");

        await _operatorAlerts.AlertAsync(
            _journal,
            MonitoringAuditActions.AlertResolved,
            AuditSubject,
            alert,
            _text["LeaseRenewalRecoveredAlertSubject"].Value,
            _text["LeaseRenewalRecoveredAlertBody"].Value,
            cancellationToken);
    }
}
