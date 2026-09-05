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
/// <b>Cut on a text element, never on a UTF-16 unit.</b> <see cref="string.Length"/> counts UTF-16
/// code units while the column counts characters, so slicing at a fixed index can split a surrogate
/// pair and leave a lone surrogate — which is not valid UTF-8 and is not a string the database or
/// the sessions screen should ever be handed. The header is entirely under the caller's control, so
/// that boundary is reachable on purpose rather than by accident, and combining marks are kept with
/// the character they belong to for the same reason.
/// </para>
/// <para>
/// The value is capped rather than refused: an odd user agent is not a reason to fail a sign-in, and
/// the field is evidence for an operator reading a session list, not an input the panel acts on.
/// Nothing here makes it safe to render as markup — that remains the SPA's escaping, as it is for
/// every other caller-supplied string.
/// </para>
/// </remarks>
public static class UserAgentText
{
    /// <summary>The longest user agent this panel stores, in characters.</summary>
    /// <remarks>The EF configurations read this, so the column and the cap cannot drift apart.</remarks>
    public const int MaxLength = 512;

    /// <summary>Shortens a user agent to <see cref="MaxLength"/> without splitting a character.</summary>
    /// <param name="userAgent">The header value exactly as the caller sent it.</param>
    /// <returns>The value, or its first <see cref="MaxLength"/> text elements.</returns>
    public static string Capped(string userAgent)
    {
        if (userAgent.Length <= MaxLength)
        {
            return userAgent;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(userAgent);
        var taken = 0;
        var end = 0;

        while (enumerator.MoveNext() && taken < MaxLength)
        {
            taken++;
            end = enumerator.ElementIndex + ((string)enumerator.Current).Length;
        }

        return userAgent[..end];
    }
}
