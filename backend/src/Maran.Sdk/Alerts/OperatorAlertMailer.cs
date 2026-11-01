using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Sdk.Alerts;

/// <summary>
/// The panel's one path for an alert addressed to the server's operator: journal it, find where
/// operator alerts go, and ask for the mail — or journal that there was nobody to tell (issue #290).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is shared.</b> Monitoring's alert evaluator and Backups' alert mailer each did these
/// three steps themselves, and neither module may reference the other. A difference between the
/// two copies — one journalling a missing recipient as a failure, one forgetting to journal it at
/// all — would be invisible in either module's own tests, so the steps live here once and both
/// modules call them.
/// </para>
/// <para>
/// <b>What stays with the caller.</b> The words: only the publisher knows what the mail is about,
/// so the subject and body arrive already localized. And the journal: the entries are written
/// through the caller's own <see cref="ISystemAuditJournal"/>, so they carry that module's actor
/// exactly as before. This class holds no mailer and no SMTP settings; it reads the ADDRESS through
/// <see cref="IAlertRecipientDirectory"/> and publishes <see cref="SendMailRequested"/>, which the
/// Notifications module sends and journals (rules/architecture.md "A shared facility's contract").
/// </para>
/// <para>
/// <b>The journal entry comes first and is written whatever happens to the mail.</b> "The disk
/// filled at 02:14" or "last night's backup of cust01 failed" is a fact about the server; whether a
/// mail about it was delivered is a fact about somebody else's mail server, and an operator
/// investigating needs the first even when the second never happened.
/// </para>
/// <para>
/// <b>Nobody to tell is <see cref="AuditActions.MailSkippedNoSmtp"/>, SUCCEEDED.</b> Outgoing mail
/// not being configured is the ordinary state of a fresh installation, and skipping the send is the
/// correct thing to do rather than a thing that failed. Recorded as a failure it put a red row in
/// the journal of every freshly installed server within minutes, and an operator who learns that
/// red rows are normal has lost the one thing the journal is for (issue #71). A third outcome of its
/// own would say it better, and that is a change to the journal's schema and every reader of it.
/// </para>
/// <para>
/// <b>Published, not sent.</b> Whether the mail server accepted the mail is the sending module's
/// business and is journalled there. Publishing also keeps an unattended round — a metrics sample,
/// a backup sweep — off a multi-second SMTP round trip to somebody else's server.
/// </para>
/// </remarks>
public sealed class OperatorAlertMailer
{
    /// <summary>Where an operator alert is addressed, read from the module that owns mail settings.</summary>
    private readonly IAlertRecipientDirectory _recipients;

    /// <summary>The bus the request to send is published on, for the Notifications module to pick up.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the mailer.</summary>
    /// <param name="recipients">Where an operator alert is addressed.</param>
    /// <param name="bus">The bus the request to send is published on.</param>
    public OperatorAlertMailer(IAlertRecipientDirectory recipients, IMessageBus bus)
    {
        _recipients = recipients;
        _bus = bus;
    }

    /// <summary>Journals one operator alert and asks for its mail.</summary>
    /// <param name="journal">The calling module's journal, which the entries are written through.</param>
    /// <param name="action">
    /// What the alert records — <see cref="AuditActions.AlertRaised"/> or
    /// <see cref="AuditActions.AlertResolved"/>.
    /// </param>
    /// <param name="auditSubject">The journal subject, in the calling module's own form.</param>
    /// <param name="mailSubject">The localized subject line.</param>
    /// <param name="mailBody">The localized body.</param>
    /// <param name="cancellationToken">Cancels the journal writes and the recipient read.</param>
    /// <returns>A task that completes once the alert is journalled and the mail requested or skipped.</returns>
    public async Task AlertAsync(
        ISystemAuditJournal journal,
        string action,
        string auditSubject,
        string mailSubject,
        string mailBody,
        CancellationToken cancellationToken)
    {
        await journal.RecordSystemAsync(action, auditSubject, succeeded: true, cancellationToken);

        var recipient = await _recipients.GetAlertRecipientAsync(cancellationToken);
        if (recipient is null)
        {
            await journal.RecordSystemAsync(
                AuditActions.MailSkippedNoSmtp, auditSubject, succeeded: true, cancellationToken);
            return;
        }

        await _bus.PublishAsync(new SendMailRequested(recipient, mailSubject, mailBody));
    }
}
