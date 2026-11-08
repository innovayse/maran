namespace Maran.Agent.Client.Services.MailService;

/// <summary>The mail stack as the agent observed it, carried by every lifecycle call.</summary>
/// <remarks>
/// Every field is what the agent MEASURED on the host, never what the panel asked for: a status built
/// from the panel's own intention would agree with the panel by construction and certify daemons nobody
/// looked at. A fact the agent could not establish is <c>null</c> or <see cref="AgentMailObservation.NotObserved"/>
/// and never a zero standing in for "none".
/// </remarks>
/// <param name="Daemons">One entry per daemon the stack consists of, in the wire's order.</param>
/// <param name="Enabled">Whether the daemons' units are enabled and the rendered configuration is in place.</param>
/// <param name="EffectiveHostname">The server hostname the running MTA presents, read from the daemon; empty when not observed.</param>
/// <param name="TlsInUse">Whether the daemons present the panel's current certificate.</param>
/// <param name="DovecotMajor">
/// The first number of the installed IMAP server's version, or <c>null</c> when it is not installed or
/// its version was not read (the wire's zero). With <paramref name="DovecotMinor"/> it names the
/// configuration generation, which is a fact of the host and is never chosen by a caller.
/// </param>
/// <param name="DovecotMinor">The second number of the IMAP server's version, or <c>null</c> when <paramref name="DovecotMajor"/> is <c>null</c>.</param>
/// <param name="PasswordScheme">
/// The scheme this host's IMAP server verifies, and the only one the panel may hash a mailbox password
/// with; <c>null</c> when it is not known (mail not installed, or an older agent).
/// </param>
/// <param name="PasswordSchemeVerified">
/// Whether the running IMAP server actually lists <paramref name="PasswordScheme"/> among the schemes it
/// can verify. A distribution fact the daemon contradicts is <see cref="AgentMailObservation.No"/>, not hidden.
/// </param>
/// <param name="QueuedMessages">Messages queued, all queues together; <c>null</c> when it was not read. Null is not zero.</param>
/// <param name="SieveAvailable">Whether the Sieve interpreter is available to the IMAP server.</param>
public sealed record AgentMailStatus(
    IReadOnlyList<AgentMailDaemonStatus> Daemons,
    AgentMailObservation Enabled,
    string EffectiveHostname,
    AgentMailObservation TlsInUse,
    uint? DovecotMajor,
    uint? DovecotMinor,
    AgentMailPasswordScheme? PasswordScheme,
    AgentMailObservation PasswordSchemeVerified,
    uint? QueuedMessages,
    AgentMailObservation SieveAvailable);
