using System.Net.Mail;

namespace Maran.SharedKernel.Utilities.Mail;

/// <summary>
/// What this panel accepts as a bare e-mail address. The single definition every module shares, so
/// the first administrator's address, the ACME account's contact, the SMTP sender, the alert
/// recipient and a test message's destination cannot drift into accepting different things.
/// </summary>
/// <remarks>
/// <para>
/// <b>One home, because three modules once answered this question differently.</b> Identity took
/// FluentValidation's <c>.EmailAddress()</c> — which in its ASP.NET-compatible mode asks only for an
/// <c>@</c> with something on either side — Ssl took a bare <c>[EmailAddress]</c> annotation with no
/// ceiling at all, and only this rule refused a display-name form or a header-injecting newline. A
/// definition of validity that differs per module is a definition the reviewer cannot state and the
/// operator cannot predict.
/// </para>
/// <para>
/// <b>A BARE address, and the equality check is what enforces that.</b> The framework's parser
/// happily accepts <c>Ops Team &lt;ops@example.com&gt;</c> and hands back the address inside it. That
/// form is a display name plus an address — two fields wearing one — and accepting it here would let
/// a name that a caller validates separately arrive through a field that does not. Requiring the
/// parse to round-trip to the original text refuses it.
/// </para>
/// <para>
/// <b>The parser is the framework's, not a regular expression.</b> Address syntax is genuinely
/// complicated, every hand-written pattern for it is wrong at the edges, and the two directions of
/// wrong are both bad: too strict refuses a legitimate operator's address, too loose lets a value
/// through that the mail server then rejects at send time — after the settings were saved and the
/// screen said they were fine.
/// </para>
/// <para>
/// <b><see cref="MaximumLength"/> is the standard's ceiling, not a storage bound.</b> A caller whose
/// column is shorter states its own <c>MaximumLength</c> as well — that is a fact about the table,
/// and this type has no business knowing it.
/// </para>
/// </remarks>
public static class EmailAddressRule
{
    /// <summary>The longest an address may be: the standard's local part plus separator plus domain.</summary>
    public const int MaximumLength = 320;

    /// <summary>Whether a candidate is one bare, deliverable-looking address.</summary>
    /// <param name="candidate">The value as the administrator typed it.</param>
    /// <returns>True when it parses as exactly one address and is written exactly as the parser reads it back.</returns>
    public static bool IsAddress(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaximumLength)
        {
            return false;
        }

        if (!MailHeaderTextRule.IsHeaderSafe(candidate))
        {
            return false;
        }

        return MailAddress.TryCreate(candidate, out var parsed)
            && string.Equals(parsed.Address, candidate, StringComparison.Ordinal);
    }
}
