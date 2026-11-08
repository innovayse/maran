namespace Maran.Agent.Client.Services.MailService;

/// <summary>The password-hash scheme this host's IMAP server verifies, and so the one the panel must hash with.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailPasswordScheme</c>. The scheme is a fact of the host's
/// distribution, never a choice of the panel: a hash in a scheme the IMAP server was not built with is
/// accepted, stored, and then refuses the customer's every login. The wire's UNSPECIFIED has no member
/// here; it is carried as <c>null</c> on <see cref="AgentMailStatus.PasswordScheme"/>, meaning "not
/// known" (mail not installed, or an older agent).
/// </remarks>
public enum AgentMailPasswordScheme
{
    /// <summary>Argon2id in its encoded form (<c>$argon2id$v=19$m=...,t=...,p=...$salt$hash</c>); preferred where the IMAP server was built with the library.</summary>
    Argon2id = 1,

    /// <summary>bcrypt (<c>$2y$cost$...</c>), which every build of the IMAP server verifies; the fallback where Argon2id is not built in.</summary>
    BlowfishCrypt = 2,
}
