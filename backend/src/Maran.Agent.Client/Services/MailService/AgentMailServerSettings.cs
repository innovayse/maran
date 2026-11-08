using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Everything one enabling of the mail stack carries: the panel's numbers, never the agent's.</summary>
/// <remarks>
/// The agent owns no product default for any of these, so every value is the panel's. The one secret is
/// held in a non-printing wrapper, so the compiler-generated <c>ToString()</c> of this record prints
/// <c>[redacted]</c> for it; a member added later inherits that protection only if it is wrapped the same way.
/// </remarks>
/// <param name="Hostname">The name the server presents to the world and the name its certificate is for; fully qualified.</param>
/// <param name="Pop3Enabled">Whether the POP3 service runs.</param>
/// <param name="AntivirusEnabled">Whether the virus scanner runs and scans incoming mail.</param>
/// <param name="MessageSizeMib">The largest message the MTA accepts, in MiB; zero is refused by the agent.</param>
/// <param name="SpamGreylistScore">The spam score from which a message is delayed once; below <paramref name="SpamAddHeaderScore"/>.</param>
/// <param name="SpamAddHeaderScore">The score from which a message is treated as spam; below <paramref name="SpamRejectScore"/>.</param>
/// <param name="SpamRejectScore">The score from which a message is refused at the SMTP stage.</param>
/// <param name="AllowExternalForwarding">Whether an alias may forward to an address on another host.</param>
/// <param name="WebmailMasterHash">
/// The hash of the webmail master secret, in the scheme the host reports; <c>null</c> means webmail is
/// not enabled and no master login exists. Unwrapped exactly once, onto the wire.
/// </param>
public sealed record AgentMailServerSettings(
    string Hostname,
    bool Pop3Enabled,
    bool AntivirusEnabled,
    uint MessageSizeMib,
    uint SpamGreylistScore,
    uint SpamAddHeaderScore,
    uint SpamRejectScore,
    bool AllowExternalForwarding,
    SensitiveString? WebmailMasterHash);
