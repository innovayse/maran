using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Resources;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Notifications.Commands.SendTestMail;

/// <summary>Handles <see cref="SendTestMailCommand"/> by sending one fixed message and journalling what happened.</summary>
/// <remarks>
/// <para>
/// <b>This is the one path where a mail failure is REPORTED to a caller rather than only
/// journalled.</b> An administrator pressed a button to find out whether mail works, so the answer
/// to "it does not" is a refusal they can read — which is the opposite of the background sender,
/// where nobody is waiting and a failure has nowhere to go but the journal.
/// </para>
/// <para>
/// <b>The two failures are journalled under different actions.</b> "You have not configured a mail
/// server" and "your mail server refused" send an operator to two different places, and a single
/// entry saying the mail failed would send them to the wrong one about half the time.
/// </para>
/// <para>
/// <b>The mail server's own words never reach the caller.</b> They are logged inside the mailer; the
/// response carries a code and its localized sentence, which is all a customer-facing surface may
/// say (rules/security.md item 8) — and this surface is administrator-only only by policy, which is
/// not the kind of thing to build an exception on.
/// </para>
/// </remarks>
public sealed class SendTestMailCommandHandler
{
    /// <summary>Resource key of the test message's subject line in <c>NotificationMessages.resx</c>.</summary>
    /// <remarks>
    /// A key, not a sentence. The words live in the resx triple in three languages, because every
    /// user-facing string the backend produces does (rules/csharp.md "The backend owns all
    /// user-facing message text"); <c>NotificationMessages</c> carries no generated class, so the key
    /// is spelled here as a named constant rather than scattered as a literal.
    /// </remarks>
    private const string TestMailSubjectKey = "TestMailSubject";

    /// <summary>Resource key of the test message's body in <c>NotificationMessages.resx</c>.</summary>
    private const string TestMailBodyKey = "TestMailBody";

    /// <summary>The seam every mail goes through.</summary>
    private readonly IMailer _mailer;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>The subject and body of the test message, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the handler.</summary>
    /// <param name="mailer">The seam every mail goes through.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="text">The localized subject and body of the test message.</param>
    public SendTestMailCommandHandler(
        IMailer mailer,
        NotificationsAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _mailer = mailer;
        _journal = journal;
        _text = text;
    }

    /// <summary>Sends the test message.</summary>
    /// <param name="command">The validated recipient.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>Success once the mail server has accepted it, or the typed failure the mailer reported.</returns>
    public async Task<Result<bool>> HandleAsync(SendTestMailCommand command, CancellationToken cancellationToken)
    {
        var subject = _text[TestMailSubjectKey].Value;
        var body = _text[TestMailBodyKey].Value;

        var sent = await _mailer.SendAsync(command.Recipient, subject, body, cancellationToken);

        if (sent.IsSuccess)
        {
            await _journal.RecordRequestAsync(
                AuditActions.TestMailSent,
                command.Recipient,
                command.IpAddress,
                command.UserAgent,
                succeeded: true,
                cancellationToken);

            return sent;
        }

        var action = sent.Error!.Code == nameof(ErrorMessages.SmtpNotConfigured)
            ? AuditActions.MailSkippedNoSmtp
            : AuditActions.MailSendFailed;

        await _journal.RecordRequestAsync(
            action,
            command.Recipient,
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return sent;
    }
}
