using System.Globalization;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// The rule for the caller-supplied <c>User-Agent</c> this panel records beside a session and an
/// audit entry: how long it may be, and how a longer one is shortened.
/// </summary>
/// <remarks>
/// <para>
/// <b>The length is here because a column and a truncation that disagree are a broken pair.</b> The
/// bound belonged to five places at once — <c>SessionConfiguration</c>, <c>AuditEventConfiguration</c>,
/// the <c>character varying(512)</c> columns behind them, and a private helper copied into four
/// controllers. Raise the column and every copy of the truncation keeps cutting at the old width,
/// silently, because nothing relates them.
/// </para>
/// <para>
/// <b>The two columns are one constant, not two that share a value.</b> <c>Sessions.UserAgent</c> and
/// <c>AuditEvents.UserAgent</c> are filled from the same accessor with the same header of the same
/// request — a sign-in writes both — so a width that applied to one and not the other would mean the
/// journal and the session list disagreeing about what the caller called itself. They are the same
/// fact stored twice, and they get the same constant.
/// </para>
/// <para>
/// <b>The cap counts what the column counts: code points.</b> <c>character varying(512)</c> is
/// measured by PostgreSQL in characters, and a character there is a code point. Counting text
/// elements instead does not bound it — one family emoji built from ZWJ is a single text element
/// and seven code points, so 512 of them are 3584 characters arriving at a 512-character column,
/// and PostgreSQL answers 22001 rather than truncating. CR LF makes the same point without an
/// emoji in sight: it is one text element and two characters, so a pure-ASCII header could come
/// back 513 characters long. The header is attacker-chosen, so an unbounded cap is a way for a
/// caller to make their OWN audit entry unwritable — the save that fails is the one writing the
/// session and the journal row together.
/// </para>
/// <para>
/// <b>Cut on a text element, never on a UTF-16 unit.</b> <see cref="string.Length"/> counts UTF-16
/// code units while the column counts characters, so slicing at a fixed index can split a surrogate
/// pair and leave a LONE SURROGATE. That is not encodable as UTF-8: .NET's default
/// <c>Encoding.UTF8</c> silently substitutes U+FFFD, while Npgsql's write buffer holds a
/// <c>UTF8Encoding</c> with an <c>EncoderExceptionFallback</c> and throws instead. The throw is the
/// one that matters — it happens inside the save that writes the session AND the audit entry, so a
/// caller could fail their own sign-in and, more to the point, keep it out of the journal, by
/// choosing their <c>User-Agent</c>. The header is entirely under the caller's control, so that
/// boundary is reachable on purpose rather than by accident, and combining marks are kept with the
/// character they belong to for the same reason.
/// </para>
/// <para>
/// <b>When the two rules disagree, the column wins.</b> A single grapheme cluster can on its own
/// exceed the cap — a ZWJ chain has no bound — so "keep every cluster whole" and "never exceed 512
/// characters" cannot both hold for every input. Overflowing loses the whole row; splitting a
/// cluster spoils one glyph at the end of a diagnostic field. So the cut falls back to a code point
/// boundary inside the cluster, which is still never inside a surrogate pair, and that is the
/// property the save actually depends on.
/// </para>
/// <para>
/// The value is capped rather than refused: an odd user agent is not a reason to fail a sign-in, and
/// the field is evidence for an operator reading a session list, not an input the panel acts on.
/// Nothing here makes it safe to render as markup — that remains the SPA's escaping, as it is for
/// every other caller-supplied string.
/// </para>
/// <para>
/// <b>It lives in SharedKernel because it is not one module's.</b> Every module's controllers write
/// the caller's client into an audit command through <c>BaseApiController.CallerUserAgent</c>, and a
/// module may not import another module's types, so the first of the four disqualification tests
/// (rules/csharp.md) settles it: not module-specific, therefore <c>Utilities/&lt;Subject&gt;/</c>. It
/// is a pure function of its argument and of one constant, so the remaining three keep it out of
/// <c>Services/</c>, <c>Domain/</c> and <c>Controllers/</c>. It is filed under <c>Network/</c> beside
/// <see cref="ClientAddress"/>, the other half of what the panel records about a caller.
/// </para>
/// </remarks>
public static class UserAgentText
{
    /// <summary>The longest user agent this panel stores, in characters.</summary>
    /// <remarks>The EF configurations read this, so the column and the cap cannot drift apart.</remarks>
    public const int MaxLength = 512;

    /// <summary>Shortens a user agent to <see cref="MaxLength"/> without splitting a character.</summary>
    /// <param name="userAgent">The header value exactly as the caller sent it.</param>
    /// <returns>The value, or a prefix of it holding at most <see cref="MaxLength"/> characters.</returns>
    public static string Capped(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        // A string of N UTF-16 units holds at most N code points, so this fast path cannot let an
        // over-long value through; it only skips the walk for the values that are obviously short.
        if (userAgent.Length <= MaxLength)
        {
            return userAgent;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(userAgent);
        var taken = 0;
        var end = 0;

        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var points = CodePointsIn(element);

            if (taken + points > MaxLength)
            {
                return taken == 0
                    ? FirstCodePoints(element, MaxLength)
                    : userAgent[..end];
            }

            taken += points;
            end = enumerator.ElementIndex + element.Length;
        }

        return userAgent[..end];
    }

    /// <summary>Counts the code points in one text element.</summary>
    /// <param name="element">A single grapheme cluster.</param>
    /// <returns>The number of characters PostgreSQL will charge the column for it.</returns>
    private static int CodePointsIn(string element)
    {
        var runes = element.EnumerateRunes();
        var points = 0;

        while (runes.MoveNext())
        {
            points++;
        }

        return points;
    }

    /// <summary>Takes the first code points of a single over-long text element.</summary>
    /// <param name="element">A grapheme cluster that alone exceeds the cap.</param>
    /// <param name="limit">The number of code points to keep.</param>
    /// <returns>A prefix of <paramref name="element"/> cut on a code point boundary.</returns>
    /// <remarks>
    /// Reached only by a cluster longer than the whole cap, which no real client sends and a
    /// hostile one can. <see cref="System.Text.Rune"/> boundaries are surrogate-pair boundaries, so
    /// the prefix stays encodable however deliberate the input was.
    /// </remarks>
    private static string FirstCodePoints(string element, int limit)
    {
        var runes = element.EnumerateRunes();
        var end = 0;
        var taken = 0;

        while (taken < limit && runes.MoveNext())
        {
            taken++;
            end += runes.Current.Utf16SequenceLength;
        }

        return element[..end];
    }
}
