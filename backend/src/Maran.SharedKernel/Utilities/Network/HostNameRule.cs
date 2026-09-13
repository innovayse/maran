using System.Text.RegularExpressions;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// What this panel accepts as a DNS host name. The single definition every module shares, so a
/// site's domain, an alias, a certificate's subject and an account's primary domain cannot drift
/// into accepting different things.
/// </summary>
/// <remarks>
/// <para>
/// <b>One home, because four call sites once answered this question separately.</b> Sites and both
/// Ssl commands each carried their own copy of the same expression, correctly anchored; Accounts
/// wrote a fifth copy inline with <c>^…$</c>. The copies were identical apart from those two
/// characters, which is the worst shape a duplicate can take — a reviewer comparing them reads the
/// same rule four times and the one difference that matters is invisible.
/// </para>
/// <para>
/// <b><c>\A…\z</c>, never <c>^…$</c>.</b> In .NET <c>$</c> also matches immediately before a
/// trailing newline, so <c>example.com\n</c> satisfies a <c>$</c>-anchored pattern. A host name is
/// written into an nginx <c>server_name</c> directive and becomes a path segment, and one embedded
/// newline turns a single directive into two — the config-file injection rules/security.md item 4
/// exists to refuse. <c>\z</c> matches only the true end of the input, so the newline is refused
/// with the rest of the alphabet.
/// </para>
/// <para>
/// <b>Shape only; the ceiling is the caller's.</b> <see cref="IsHostName"/> checks the alphabet,
/// the label shape and the anchors, and deliberately does not check the total length —
/// <see cref="MaximumLength"/> is stated by each call site as its own rule so that an over-long
/// name is reported as "too long" rather than "malformed", and so the two checks are genuinely two
/// checks rather than one and a decoration (rules/testing.md, on masking pairs).
/// </para>
/// <para>
/// <b>It lives in <c>SharedKernel</c> rather than <c>Maran.Sdk</c> because it is pure BCL.</b> The
/// rule is a regular expression over a string and knows nothing of FluentValidation, ASP.NET or
/// HTTP, so it belongs in the project that references no framework at all; only a
/// validation-framework-aware helper would belong in <c>Maran.Sdk</c>, which carries the
/// <c>Microsoft.AspNetCore.App</c> framework reference. Putting it here also keeps it reachable
/// from a place that must never take a framework dependency to ask a question this small.
/// </para>
/// </remarks>
public static partial class HostNameRule
{
    /// <summary>The longest a host name may be, from DNS. Stated by each caller as its own rule.</summary>
    public const int MaximumLength = 253;

    /// <summary>
    /// The pattern itself: two or more DNS labels of 1–63 characters, no leading or trailing hyphen
    /// on any label, anchored so that nothing — a newline above all — may follow the last one.
    /// </summary>
    private const string Pattern =
        @"\A(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+\z";

    /// <summary>Whether a candidate has the shape of a DNS host name.</summary>
    /// <param name="candidate">The value as the caller submitted it.</param>
    /// <returns>True when it is two or more well-formed labels and nothing else, newline included.</returns>
    public static bool IsHostName(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return HostName().IsMatch(candidate);
    }

    /// <summary>The compiled matcher for <see cref="Pattern"/>.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(Pattern)]
    private static partial Regex HostName();
}
