namespace Maran.Modules.Notifications.Interfaces;

/// <summary>
/// Sends one message through the panel's configured mail server. Module-internal: it exists so that
/// everything which sends mail — the alert evaluator, the test-mail command, the handler for other
/// modules' <c>SendMailRequested</c> — talks to one seam, and so that the only file naming a mail
/// library is the one implementation behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It returns a result and does not throw.</b> A mail server that refuses a message or cannot be
/// reached is an ordinary, expected outcome on somebody else's infrastructure — not a bug in this
/// process — so it travels as a typed <see cref="Error"/> the way an agent failure does
/// (rules/csharp.md "Errors: Result, not exceptions"). That matters most for the background sender:
/// R11 requires that a token-bearing mail request never reach Wolverine's dead-letter machinery,
/// and the surest way to guarantee that is for the layer below the handler not to throw either.
/// </para>
/// <para>
/// <b>No settings travel through it.</b> An implementation reads the panel's own SMTP configuration
/// for itself, so no caller can be given — or accidentally hand it — a different server, a different
/// sender, or a credential.
/// </para>
/// </remarks>
public interface IMailer
{
    /// <summary>Sends one message.</summary>
    /// <param name="recipient">The address to deliver to.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="body">The plain-text body.</param>
    /// <param name="cancellationToken">Cancellation token for the send.</param>
    /// <returns>
    /// Success once the server has accepted the message; <c>SmtpNotConfigured</c> when the panel has
    /// no mail settings at all, <c>MailDeliveryFailed</c> (502) when the server answered and refused
    /// it, and <c>MailServerUnreachable</c> (503) when no server answered in time. They are three
    /// codes because they need separate audit entries and separate answers to the operator: "you have
    /// not set this up", "your provider said no", and "nothing answered, try again later".
    /// </returns>
    Task<Result<bool>> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}
