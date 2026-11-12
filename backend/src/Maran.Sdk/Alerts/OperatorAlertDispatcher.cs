using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Sdk.Alerts;

/// <summary>
/// The panel's one path for an alert addressed to the server's operator: journal it, then fan it out
/// to every output the operator may have chosen - mail, the alert webhook, the notification feed
/// (issues #290, #142, #164).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is shared.</b> Monitoring's alert evaluator, Backups' and Ssl's alert mailers each did
/// these steps themselves, and no module may reference another. A difference between the copies would
/// be invisible in any module's own tests, so the steps live here once and every module calls them.
/// Each new output is one more publish here, never a new call site in a module.
/// </para>
/// <para>
/// <b>What stays with the caller.</b> The words: only the publisher knows what the alert is about, so
/// the mail subject and body arrive already localized. And the journal: entries are written through
/// the caller's own <see cref="ISystemAuditJournal"/>, so they carry that module's actor. This class
/// holds no mailer, no webhook sender and no settings; it reads the ADDRESSES through
/// <see cref="IAlertRecipientDirectory"/> and publishes messages the Notifications module handles
/// (rules/architecture.md "A shared facility's contract").
/// </para>
/// <para>
/// <b>Order: the recipients are read first, then the journal entry, then the outputs, independent of one
/// another.</b> The one step that can fail for a reason of the panel's own (the settings read) comes before anything
/// is written or published, so a caller that retries after a failure repeats nothing: no second journal row, no
/// second webhook event, no second bell entry. After that the journal entry comes before the outputs: "the disk
/// filled at 02:14" is a fact about the server; whether a mail, a webhook or a bell entry about it was delivered is
/// a fact about somebody else's system, and an operator investigating needs the first even when the rest never
/// happened. The outputs are separate publishes onto local queues: publishing only enqueues, the handlers run later
/// on their own, so a handler that fails never reaches this method and one output cannot block another. The webhook
/// and the feed entry are published whether or not any mail address exists.
/// </para>
/// <para>
/// <b>No address is <see cref="OutgoingMailAuditActions.MailSkippedNoSmtp"/>, SUCCEEDED.</b> Outgoing mail not
/// being configured is the ordinary state of a fresh installation, and skipping the send is correct
/// rather than a failure. Recorded as a failure it put a red row in the journal of every freshly
/// installed server within minutes, and an operator who learns that red rows are normal has lost the
/// one thing the journal is for (issue #71).
/// </para>
/// <para>
/// <b>Published, not sent.</b> Whether the mail server accepted the mail is the sending module's
/// business and is journalled there. Publishing also keeps an unattended round - a metrics sample, a
/// backup sweep - off a multi-second round trip to somebody else's server.
/// </para>
/// </remarks>
public sealed class OperatorAlertDispatcher
{
    /// <summary>Where operator alerts are addressed, read from the module that owns mail settings.</summary>
    private readonly IAlertRecipientDirectory _recipients;

    /// <summary>The bus every output is published on, for the Notifications module to pick up.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock, which stamps the webhook and feed messages.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the dispatcher.</summary>
    /// <param name="recipients">Where operator alerts are addressed.</param>
    /// <param name="bus">The bus the outputs are published on.</param>
    /// <param name="clock">The panel's clock.</param>
    public OperatorAlertDispatcher(IAlertRecipientDirectory recipients, IMessageBus bus, IClock clock)
    {
        _recipients = recipients;
        _bus = bus;
        _clock = clock;
    }

    /// <summary>Journals one operator alert and publishes it to every output.</summary>
    /// <param name="journal">The calling module's journal, which the entries are written through.</param>
    /// <param name="action">
    /// What the alert records - <see cref="MonitoringAuditActions.AlertRaised"/> or
    /// <see cref="MonitoringAuditActions.AlertResolved"/>.
    /// </param>
    /// <param name="auditSubject">The journal subject, in the calling module's own form.</param>
    /// <param name="alert">The alert, described without words: what, about which thing, which figure.</param>
    /// <param name="mailSubject">The localized subject line.</param>
    /// <param name="mailBody">The localized body.</param>
    /// <param name="cancellationToken">Cancels the recipient read and the journal writes.</param>
    /// <returns>A task that completes once the alert is journalled and every output published or skipped.</returns>
    /// <exception cref="Exception">
    /// The recipient read failed: nothing was journalled or published, so the caller may try the same alert again.
    /// </exception>
    public async Task AlertAsync(
        ISystemAuditJournal journal,
        string action,
        string auditSubject,
        OperatorAlert alert,
        string mailSubject,
        string mailBody,
        CancellationToken cancellationToken)
    {
        var recipients = await _recipients.GetAlertRecipientsAsync(cancellationToken);

        await journal.RecordSystemAsync(action, auditSubject, succeeded: true, cancellationToken);

        var now = _clock.UtcNow;
        await _bus.PublishAsync(new OperatorAlertRaised(alert, now));
        await _bus.PublishAsync(FeedEntryFor(alert, now));

        if (recipients.Count == 0)
        {
            await journal.RecordSystemAsync(
                OutgoingMailAuditActions.MailSkippedNoSmtp, auditSubject, succeeded: true, cancellationToken);
            return;
        }

        foreach (var recipient in recipients)
        {
            await _bus.PublishAsync(new SendMailRequested(recipient, mailSubject, mailBody, MailPurpose.OperatorAlert));
        }
    }

    /// <summary>Maps an alert onto the notification-feed entry that announces it.</summary>
    /// <param name="alert">The alert.</param>
    /// <param name="now">When it was handled.</param>
    /// <returns>
    /// The feed message: critical for a condition that began, informational for one that ended, a
    /// warning for a one-off event. The feed values (the detail unless the alert carries its own) travel as the
    /// arguments and the destination is the page of the module that raised it.
    /// </returns>
    private static PanelNotificationRaised FeedEntryFor(OperatorAlert alert, DateTimeOffset now)
    {
        var source = alert.Source == AlertSource.Backups ? NotificationSource.Backup : NotificationSource.Alert;
        var severity = alert.Transition switch
        {
            AlertTransition.Raised => NotificationSeverity.Critical,
            AlertTransition.Resolved => NotificationSeverity.Info,
            _ => NotificationSeverity.Warning,
        };
        var deepLink = alert.Source switch
        {
            AlertSource.Monitoring => NotificationDeepLinks.Monitoring,
            AlertSource.Backups => NotificationDeepLinks.Backups,
            AlertSource.Licensing => NotificationDeepLinks.Licence,
            _ => null,
        };

        return new PanelNotificationRaised(source, alert.Code, severity, alert.Subject, alert.FeedValues ?? [alert.Detail], deepLink, now);
    }
}
