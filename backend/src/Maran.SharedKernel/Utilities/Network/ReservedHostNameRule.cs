namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// Which host names no tenant may put into a vhost or a certificate request, whatever their shape:
/// address literals, the loopback family and special-use suffixes. The STATIC half of the reserved
/// set; the panel's own name is host-specific and is answered through
/// <c>Maran.Sdk.Interfaces.IReservedHostDirectory</c> instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sibling of <see cref="HostNameRule"/>, never a replacement.</b> <see cref="HostNameRule"/>
/// answers "is this shaped like a host name"; this rule answers "is this a name a tenant may claim".
/// A caller applies both, each with its own message, because <c>203.0.113.7</c> is well-formed and
/// still must not reach an nginx <c>server_name</c> or an ACME order. The agent revalidates the same
/// set in its validated <c>Domain</c> type (rules/security.md item 1), and the two spellings must
/// stay identical: docs/superpowers/notes/2026-10-26-reserved-hostnames-threat-note.md is the
/// contract both implement.
/// </para>
/// <para>
/// <b>The set.</b> A name is reserved when, after lower-casing and dropping ONE trailing dot, (1) its
/// final label is all ASCII digits (every dotted-quad IPv4 literal, and the short forms
/// <c>1.2.3</c>, <c>10.1</c> and <c>12345</c> that <c>inet_aton</c> semantics read as addresses);
/// (2) it is <c>localhost</c> or ends in <c>.localhost</c>; or (3) it ends in one of the special-use
/// suffixes <c>.local</c>, <c>.localdomain</c>, <c>.internal</c>, <c>.lan</c>, <c>.home.arpa</c>,
/// <c>.invalid</c>. <c>.test</c> and <c>.example</c> are deliberately NOT reserved, so fixtures and
/// staging names keep working. The numeric rule is categorical on purpose: no list of the server's
/// addresses is needed, and a list would go stale.
/// </para>
/// <para>
/// <b>Not a shape check.</b> The rule is pure ordinal string comparison and does not validate the
/// alphabet; it is meant to run on a value <see cref="HostNameRule.IsHostName"/> has already
/// accepted, which excludes non-ASCII digits and newlines. A null or empty candidate is NOT
/// reserved: those are the shape rule's and the required-field rule's concern, so a request is
/// never reported twice for one fault. Comparison is ordinal and case-insensitive, never
/// culture-sensitive.
/// </para>
/// </remarks>
public static class ReservedHostNameRule
{
    /// <summary>The loopback name itself, reserved bare and as a parent of any label.</summary>
    private const string Localhost = "localhost";

    /// <summary>
    /// The one special-use suffix that is a two-label name and so passes <see cref="HostNameRule"/>
    /// bare; the agent refuses it bare too, so the two sides agree. The other suffixes are a single
    /// label, which the shape rule already rejects.
    /// </summary>
    private const string BareSpecialUse = "home.arpa";

    /// <summary>The suffixes, each with its leading dot, that never name a public host.</summary>
    private static readonly string[] SpecialUseSuffixes =
    [
        ".localhost",
        ".local",
        ".localdomain",
        ".internal",
        ".lan",
        ".home.arpa",
        ".invalid",
    ];

    /// <summary>Whether a candidate belongs to the static reserved set.</summary>
    /// <param name="candidate">The host name as the caller submitted it.</param>
    /// <returns>
    /// True when the name is an address-shaped, loopback or special-use name; false for any other
    /// name and for null or empty input.
    /// </returns>
    public static bool IsReserved(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var name = candidate.EndsWith('.') ? candidate[..^1] : candidate;
        if (name.Length == 0)
        {
            return false;
        }

        if (HasNumericFinalLabel(name))
        {
            return true;
        }

        if (name.Equals(Localhost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals(BareSpecialUse, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var suffix in SpecialUseSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the last dot-separated label consists only of ASCII digits.</summary>
    /// <param name="name">A non-empty name with no trailing dot.</param>
    /// <returns>True when the final label is non-empty and all characters are <c>0</c>-<c>9</c>.</returns>
    private static bool HasNumericFinalLabel(string name)
    {
        var label = name[(name.LastIndexOf('.') + 1)..];
        if (label.Length == 0)
        {
            return false;
        }

        foreach (var character in label)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
