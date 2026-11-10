namespace Maran.Modules.Notifications.Domain.Enums;

/// <summary>How the panel protects the connection to the mail server.</summary>
/// <remarks>
/// <para>
/// Three values rather than a boolean, because the two encrypted modes are not variants of one
/// setting: implicit TLS wraps the socket before a byte of SMTP is spoken, while STARTTLS opens in
/// the clear and upgrades. A boolean would have to pick one of them for "true", and every provider
/// that wanted the other would be configured wrongly with no way to say so.
/// </para>
/// <para>
/// <b>There is no member with the value 0, and that is the security property of this type</b>
/// (issue #491). The cleartext mode used to be 0, which is what a request that never mentioned the
/// field deserializes to, so omitting the setting sent the submission credential in the clear. Now
/// 0 names nothing: <c>default(SmtpSecurity)</c> is not a mode, the validator refuses it, and the
/// mailer reads an unrecognised stored value as <see cref="StartTls"/>. Choosing no protection is
/// the deliberate act of naming <see cref="None"/>. The encrypted modes keep the numbers 1 and 2
/// they always had, so rows that held them were not touched; the migration moved the rows that held
/// the old 0 to <see cref="None"/>'s new value.
/// </para>
/// </remarks>
public enum SmtpSecurity
{
    /// <summary>Connect in the clear and upgrade with <c>STARTTLS</c> before authenticating. The submission port's usual mode.</summary>
    StartTls = 1,

    /// <summary>Wrap the socket in TLS before speaking SMTP at all. The usual mode for port 465.</summary>
    ImplicitTls = 2,

    /// <summary>
    /// No transport security. Only ever right for a relay on the same machine (<c>127.0.0.1:25</c>),
    /// where there is no network for anything to be read from — and the credentials travel in the
    /// clear, which is why it is never a default and has to be named.
    /// </summary>
    None = 3,
}
