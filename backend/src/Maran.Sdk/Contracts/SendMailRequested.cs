namespace Maran.Sdk.Contracts;

/// <summary>
/// Asks the panel to send one message. Any module publishes it; the Notifications module, which owns
/// the SMTP settings, is the one that sends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a contract in the Sdk.</b> The module that decides a mail is needed — Identity, when
/// somebody asks to reset a password; Monitoring, when the disk fills — is not the module that
/// holds the mail server's address and credential, and neither may reference the other
/// (rules/architecture.md "Backend: modular monolith", enforced by <c>ModuleIsolationTests</c>). So
/// the message is declared here, in the surface all of them already depend on. This is the ONLY way
/// mail is asked for: <c>IMailer</c> is internal to the sending module, so no consumer can reach the
/// credential-holding seam, and no consumer is privileged over any other.
/// </para>
/// <para>
/// <b>It travels on a LOCAL, NON-DURABLE queue, and that is a security property rather than a
/// performance one.</b> A password-reset body carries a live token — permission to become the
/// account — and a durable queue would write that token into an envelope table, where it would rest
/// on disk, survive in a database dump, and outlive the token's own hour. So this message is never
/// persisted, and the two consequences are both accepted deliberately: a process that stops between
/// the publish and the send loses the mail (the user asks again), and a handler that THREW would
/// hand the same envelope to the dead-letter machinery — which is exactly the at-rest persistence
/// the local queue avoids, and is why the handler catches everything it can and returns normally.
/// </para>
/// <para>
/// <b>Publishing must not block the request that publishes.</b> Sending is a full SMTP round trip to
/// somebody else's server, measured in seconds when it is slow and in timeouts when it is broken. A
/// publisher that waited for it would leak exactly what the reset endpoint is built not to reveal:
/// a known address would take seconds while an unknown one returned instantly, which is an account
/// enumeration oracle anybody can read with a stopwatch. So the publisher publishes and answers, and
/// the send happens in the background.
/// </para>
/// <para>
/// <b>The body is already composed and already localized.</b> The publishing module renders it from
/// its own resources, in the recipient's language, because the backend owns every word a user reads
/// (rules/csharp.md) and only the publisher knows what the mail is about. Nothing downstream
/// interprets, templates, or rewrites it.
/// </para>
/// </remarks>
/// <param name="Recipient">The address to deliver to.</param>
/// <param name="Subject">The subject line, already localized by the publisher.</param>
/// <param name="Body">
/// The plain-text body, already localized by the publisher. It may contain a secret — a reset token
/// is the first example — so nothing that handles this message may log it, journal it, or attach it
/// to an error travelling outward.
/// </param>
public sealed record SendMailRequested(string Recipient, string Subject, string Body);
