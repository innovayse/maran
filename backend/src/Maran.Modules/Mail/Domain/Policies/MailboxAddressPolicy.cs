using System.Text.RegularExpressions;

namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>
/// What the panel accepts as the local part of a hosted address, and how an address is written and read.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is the agent's <c>MailLocalPart</c>, restated: lower-case letters, digits and
/// <c>.</c> <c>-</c> <c>_</c>, at most 64 characters, starting and ending with a letter or digit, no two
/// dots in a row. A plus sign is NOT in it: the MTA splits <c>name+tag</c> at the plus and delivers to
/// <c>name</c>, so a mailbox called <c>a+b</c> could never receive. The value names a directory under a
/// customer's home and a line in the login database, so it is validated against a grammar and never
/// escaped (rules/security.md items 2 and 4).
/// </para>
/// <para>
/// Case is folded by <see cref="Normalize"/> before the grammar is applied, because an address is not
/// case-sensitive in practice and two spellings of one mailbox would otherwise be two rows.
/// </para>
/// </remarks>
public static class MailboxAddressPolicy
{
    /// <summary>The marker stored as the local part of a catch-all, which no mailbox grammar can produce.</summary>
    public const string CatchAllLocalPart = "*";

    /// <summary>The local part grammar, anchored with <c>\A</c>/<c>\z</c> because <c>$</c> also matches before a trailing newline.</summary>
    private static readonly Regex LocalPartShape = new(
        @"\A[a-z0-9]([a-z0-9._-]*[a-z0-9])?\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Folds a typed local part to the one spelling the panel stores.</summary>
    /// <param name="localPart">What the caller typed.</param>
    /// <returns>The trimmed, lower-cased value; an empty string for a null input.</returns>
    public static string Normalize(string? localPart)
    {
        return (localPart ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>Whether a normalised value is an acceptable local part.</summary>
    /// <param name="localPart">The value, already passed through <see cref="Normalize"/>.</param>
    /// <returns>True when the value fits the grammar and the length ceiling.</returns>
    public static bool IsValidLocalPart(string localPart)
    {
        ArgumentNullException.ThrowIfNull(localPart);

        return localPart.Length is > 0 and <= MailDefaults.LocalPartMaxLength
            && LocalPartShape.IsMatch(localPart)
            && !localPart.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>Writes an address from its two halves.</summary>
    /// <param name="localPart">The local part.</param>
    /// <param name="domain">The mail domain.</param>
    /// <returns>The address, <c>local@domain</c>.</returns>
    public static string Compose(string localPart, string domain)
    {
        ArgumentNullException.ThrowIfNull(localPart);
        ArgumentNullException.ThrowIfNull(domain);

        return string.Concat(localPart, "@", domain);
    }

    /// <summary>Reads the domain half of an address.</summary>
    /// <param name="address">A plain address, <c>local@domain</c>.</param>
    /// <returns>The lower-cased domain, or an empty string when there is no <c>@</c>.</returns>
    public static string DomainOf(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var at = address.LastIndexOf('@');

        return at < 0 ? string.Empty : address[(at + 1)..].ToLowerInvariant();
    }
}
