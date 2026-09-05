namespace Maran.Modules.Notifications.Commands.SendTestMail;

/// <summary>Sends one fixed message through the panel's configured mail server, so an administrator can see whether it works.</summary>
/// <remarks>
/// <para>
/// <b>The recipient travels in the command, and the reason is a module boundary rather than a
/// preference.</b> The natural destination is "the administrator who clicked the button", but this
/// module cannot learn that address: panel users belong to Identity, a module may never reference
/// another module (rules/architecture.md), and the Sdk's cross-module window carries hosting-account
/// facts, not user contacts. So the administrator states the address, which is also what a test is
/// usually for — checking that mail reaches a particular mailbox.
/// </para>
/// <para>
/// <b>That makes the endpoint an administrator-only, rate-limited "send a fixed message to an address
/// of your choosing", and it is worth naming the exposure.</b> The panel's own administrator already
/// owns the SMTP credential outright and can send anything they like through it directly; what this
/// adds is convenience, not capability. The body and subject are fixed panel text, so the endpoint
/// cannot be used to compose a message.
/// </para>
/// </remarks>
/// <param name="Recipient">Where to send the test message.</param>
/// <param name="IpAddress">The caller's address, for the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal.</param>
public sealed record SendTestMailCommand(string Recipient, string IpAddress, string UserAgent);
