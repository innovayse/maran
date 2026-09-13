using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Resources;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Notifications.IntegrationEvents.Handlers;

/// <summary>
/// Sends the mail another module asked for, in the background, and never lets a failure escape.
/// </summary>
/// <remarks>
/// <para>
/// <b>This handler must not throw, and that is a security requirement rather than robustness
/// hygiene (R11).</b> A <see cref="SendMailRequested"/> body can carry a live password-reset token —
/// permission to become the account. It travels on a local, non-durable queue precisely so that
/// token is never written to disk. A handler that threw would hand the same envelope to Wolverine's
/// dead-letter machinery, which PERSISTS it: the token would then rest in a database table, survive
/// a backup, and outlive its own hour. So every failure is caught here, journalled, and swallowed.
/// The named test <c>a_mailer_failure_is_audited_and_never_thrown</c> pins it, and rethrowing is the
/// mutation that must turn it red.
/// </para>
/// <para>
/// <b>Swallowing is safe here because nothing is lost that matters.</b> The two things this could be
/// retrying are a permanent refusal, which will refuse again, and a reset mail, whose owner can ask
/// for another in ten seconds — and whose token expires anyway. What is NOT lost is the record: the
/// journal gets an entry either way, so "the reset mail never arrived" is a question the panel can
/// answer.
/// </para>
/// <para>
/// <b>The audit entry names the recipient and never the body.</b> The recipient is what an operator
/// searches for; the body is the token itself, and the journal is append-only and never deleted
/// (rules/security.md item 8). Neither is the mail server's own refusal text, which is logged inside
/// the mailer where operators — and not the journal's readers — can see it.
/// </para>
/// <para>
/// <b>Sending here, rather than inline in the publisher, is what closes the enumeration channel.</b>
/// A reset endpoint that awaited SMTP would answer in seconds for an address that exists and
/// instantly for one that does not, which anybody can read with a stopwatch. The publisher publishes
/// and returns; this runs afterwards, on its own.
/// </para>
/// </remarks>
public sealed class SendMailRequestedHandler
{
    /// <summary>Pre-compiled log delegate for a failure the journal has already recorded.</summary>
    /// <remarks>
    /// The recipient and nothing else. Not the subject, which can name what the mail was about, and
    /// above all not the body.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogNotSent =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(SendMailRequestedHandler)),
            "A requested message was not sent to {Recipient}; the request is not retried");

    /// <summary>The seam every mail goes through.</summary>
    private readonly IMailer _mailer;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>Where a failure is reported for an operator, since the journal carries no diagnostics.</summary>
    private readonly ILogger<SendMailRequestedHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="mailer">The seam every mail goes through.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="logger">Where a failure is reported for an operator.</param>
    public SendMailRequestedHandler(
        IMailer mailer,
        NotificationsAuditJournal journal,
        ILogger<SendMailRequestedHandler> logger)
    {
        _mailer = mailer;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Sends one requested message.</summary>
    /// <param name="message">What to send, and to whom.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>
    /// Always resolves, never faults. There is no return value because there is no caller: the
    /// publisher answered its own request before this ran.
    /// </returns>
    public async Task HandleAsync(SendMailRequested message, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _mailer.SendAsync(
                message.Recipient, message.Subject, message.Body, cancellationToken);

            if (sent.IsSuccess)
            {
                return;
            }

            var action = sent.Error!.Code == nameof(ErrorMessages.SmtpNotConfigured)
                ? AuditActions.MailSkippedNoSmtp
                : AuditActions.MailSendFailed;

            LogNotSent(_logger, message.Recipient, null);
            await RecordAsync(action, message.Recipient, cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately catches everything, INCLUDING cancellation. Every other background
            // component in this panel rethrows on shutdown; this one must not, because rethrowing is
            // exactly how the token-bearing envelope reaches the dead-letter store. A shutdown that
            // loses one unsent mail costs the user a second click; a token written to disk costs
            // rather more.
            LogNotSent(_logger, message.Recipient, exception);
            await RecordAsync(AuditActions.MailSendFailed, message.Recipient, CancellationToken.None);
        }
    }

    /// <summary>Journals one failed send, and refuses to fail even at that.</summary>
    /// <param name="action">Which failure it was.</param>
    /// <param name="recipient">Who the message was for.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// The journal write is itself wrapped. It reaches PostgreSQL, so it can fail — and an exception
    /// out of the audit call would escape this handler by the back door, which is the one thing the
    /// whole class exists to prevent. A journal that could not be written is reported to the log,
    /// which is the only place left to report it.
    /// </remarks>
    private async Task RecordAsync(string action, string recipient, CancellationToken cancellationToken)
    {
        try
        {
            await _journal.RecordSystemAsync(action, recipient, succeeded: false, cancellationToken);
        }
        catch (Exception exception)
        {
            LogNotSent(_logger, recipient, exception);
        }
    }
}
