using System.Globalization;

namespace Maran.SharedKernel.Utilities.Http;

/// <summary>
/// What this panel records as a caller's <c>User-Agent</c>: attacker-supplied header text, capped at
/// the width of the column that stores it and cut where a cut is safe.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cap is a fact about the schema, so it lives in one place.</b> Four controllers had written
/// <c>userAgent.Length > 512 ? userAgent[..512] : userAgent</c> privately, and two EF configurations
/// declared <c>HasMaxLength(512)</c> — five copies of one number that must agree, with nothing to
/// notice when they stop. Widen the column and the truncations silently keep cutting at the old
/// width; narrow it and every write throws. Both configurations now read
/// <see cref="MaxLength"/> from here, so there is one number and it is this one.
/// </para>
/// <para>
/// <b>The two columns are one constant, not two that share a value.</b> <c>Sessions.UserAgent</c> and
/// <c>AuditEvents.UserAgent</c> are filled from the same accessor with the same header of the same
/// request — a sign-in writes both — so a width that applied to one and not the other would mean the
/// journal and the session list disagreeing about what the caller called itself. They are the same
/// fact stored twice, and they get the same constant.
/// </para>
/// <para>
/// <b>Why cutting at <c>[..512]</c> was wrong, measured rather than assumed.</b>
/// <see cref="string.Length"/> counts UTF-16 code units and the column counts characters, so a
/// header ending in a non-BMP character across the boundary — an emoji, and the header is entirely
/// caller-controlled — leaves a LONE SURROGATE at the end. That is not encodable as UTF-8:
/// .NET's default <c>Encoding.UTF8</c> silently replaces it with U+FFFD, and Npgsql's write buffer
/// holds a <c>UTF8Encoding</c> with an <c>EncoderExceptionFallback</c>, which throws instead. The
/// throw is the one that matters: it happens inside the save that writes the session AND the audit
/// entry, so a caller could fail their own sign-in — and, more to the point, keep it out of the
/// journal — by choosing their <c>User-Agent</c>. Cutting on a text-element boundary removes the
/// question: no half of a pair, and no combining mark severed from what it combines with.
/// </para>
/// <para>
/// <b>It lives in SharedKernel because it is not one module's.</b> Every module's controllers write
/// the caller's client into an audit command and a module may not import another module's types, so
/// the first of the four disqualification tests (rules/csharp.md) settles it: not module-specific,
/// therefore <c>Utilities/&lt;Subject&gt;/</c>. It is a pure function of its argument and of one
/// constant, so the remaining three keep it out of <c>Services/</c>, <c>Domain/</c> and
/// <c>Controllers/</c>. <see cref="Maran.SharedKernel.Utilities.Mail.MailHeaderTextRule"/> is the
/// precedent it is filed beside: attacker-supplied header text, bounded by a rule rather than by
/// whoever remembers.
/// </para>
/// </remarks>
public static class UserAgentText
{
    /// <summary>
    /// The widest <c>User-Agent</c> this panel stores, in characters — the width of both
    /// <c>Sessions.UserAgent</c> and <c>AuditEvents.UserAgent</c>.
    /// </summary>
    public const int MaxLength = 512;

    /// <summary>Renders a caller's <c>User-Agent</c> header as the value the panel stores.</summary>
    /// <param name="header">The header exactly as the caller sent it; may be absent.</param>
    /// <returns>
    /// The header, or its first <see cref="MaxLength"/> text elements when it is longer. Never null,
    /// because both columns are <c>NOT NULL</c> and a client that sends no header is not an error.
    /// </returns>
    public static string Capped(string? header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return string.Empty;
        }

        // The cheap test first: a header of at most MaxLength code units is at most MaxLength text
        // elements, so it fits whatever it is made of and needs no enumeration at all.
        if (header.Length <= MaxLength)
        {
            return header;
        }

        // Counted in text elements — what the column counts is characters, and what a reader loses
        // by a bad cut is a whole grapheme rather than a code unit.
        var enumerator = StringInfo.GetTextElementEnumerator(header);
        var elements = 0;
        var end = header.Length;

        while (enumerator.MoveNext())
        {
            if (elements == MaxLength)
            {
                end = enumerator.ElementIndex;
                break;
            }

            elements++;
        }

        return header[..end];
    }
}
