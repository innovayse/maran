namespace Maran.Modules.Notifications.Domain.Enums;

/// <summary>How the panel protects the connection to the mail server.</summary>
/// <remarks>
/// Three values rather than a boolean, because the two encrypted modes are not variants of one
/// setting: implicit TLS wraps the socket before a byte of SMTP is spoken, while STARTTLS opens in
/// the clear and upgrades. A boolean would have to pick one of them for "true", and every provider
/// that wanted the other would be configured wrongly with no way to say so.
/// </remarks>
public enum SmtpSecurity
{
    /// <summary>
    /// No transport security. Only ever right for a relay on the same machine (<c>127.0.0.1:25</c>),
    /// where there is no network for anything to be read from — and the credentials travel in the
    /// clear, which is why it is never the default.
    /// </summary>
    None = 0,

    /// <summary>Connect in the clear and upgrade with <c>STARTTLS</c> before authenticating. The submission port's usual mode.</summary>
    StartTls = 1,

    /// <summary>Wrap the socket in TLS before speaking SMTP at all. The usual mode for port 465.</summary>
    ImplicitTls = 2,
}
