namespace Maran.SharedKernel.Utilities.Mail;

/// <summary>
/// What this panel accepts as a value destined for a mail header — a server name, a user name, a
/// display name, an address.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is rules/security.md item 4 applied to SMTP.</b> A mail message is a line-oriented
/// format: headers are separated by CRLF and the body begins after a blank line. A carriage return
/// or newline inside a value therefore does not corrupt one header, it INVENTS the next one — an
/// extra <c>Bcc:</c>, an extra <c>Content-Type:</c>, or an early end of the header block that turns
/// the rest of the operator's text into a body of the attacker's choosing. It is the same class of
/// defect as an embedded newline in a crontab entry, and it is refused the same way: the value is
/// validated, not escaped.
/// </para>
/// <para>
/// <b>Refused here even though the library also refuses it.</b> MimeKit encodes or rejects such a
/// value on its own, and relying on that alone would make the panel's safety a property of whichever
/// library is linked today — while giving the administrator a failed send instead of a message
/// telling them which field is wrong.
/// </para>
/// <para>
/// Every control character goes, not only CR and LF. A NUL truncates the value for anything that
/// reads it as a C string on the way, and no header field has a legitimate use for the rest.
/// </para>
/// </remarks>
public static class MailHeaderTextRule
{
    /// <summary>Whether a candidate may be written into a mail header.</summary>
    /// <param name="candidate">The value as the administrator typed it.</param>
    /// <returns>True when it carries no control character. An empty or absent value is acceptable — several of these fields are optional.</returns>
    public static bool IsHeaderSafe(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return true;
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
