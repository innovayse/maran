namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>What the panel accepts as a mailbox password.</summary>
/// <remarks>
/// <para>
/// A mailbox password is reachable from the internet through the IMAP and SMTP submission ports, so the
/// floor is higher than a panel login's: twelve characters, with no class requirements (length is what
/// resists guessing; composition rules mostly make people predictable). The ceiling keeps the hashing
/// work bounded, because the hash function is memory-hard and its cost is paid per request.
/// </para>
/// <para>
/// Control characters are refused. The password is hashed here and only the hash reaches the host, so a
/// control character could not inject anything, but a password a customer cannot type into a mail
/// client's field is a mailbox they cannot open.
/// </para>
/// </remarks>
public static class MailPasswordPolicy
{
    /// <summary>Whether a password is acceptable.</summary>
    /// <param name="password">The candidate.</param>
    /// <returns>True when its length is within bounds and it holds no control character.</returns>
    public static bool IsAcceptable(string? password)
    {
        if (password is null || password.Length < MailDefaults.PasswordMinLength
            || password.Length > MailDefaults.PasswordMaxLength)
        {
            return false;
        }

        return !password.Any(char.IsControl);
    }
}
