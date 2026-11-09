using System.Text.RegularExpressions;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>What a redirect rule's path and target may look like.</summary>
/// <remarks>
/// Both values are written into a web-server directive by a root process, so they are checked against
/// an allow-list of characters instead of a deny-list of dangerous ones: whitespace, quotes, braces,
/// semicolons, backslashes and <c>$</c> are simply not in either set (rules/security.md item 4). The agent
/// re-validates both with its own types. A target that is an absolute URL must name a well-formed host
/// over <c>http</c> or <c>https</c> and nothing else — no other scheme can be reached through a redirect
/// rule, which is what keeps it from being an open redirect to <c>javascript:</c> or <c>data:</c>.
/// </remarks>
public static partial class SiteRedirectRules
{
    /// <summary>The path the ACME challenge is answered under. A redirect must never shadow it.</summary>
    private const string AcmeChallengePrefix = "/.well-known/acme-challenge";

    /// <summary>A rooted path made only of characters that need no escaping and carry no query.</summary>
    [GeneratedRegex(@"\A/[A-Za-z0-9._~/-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PathShape();

    /// <summary>An absolute http(s) URL: host, optional port, optional path and query made of plain characters.</summary>
    [GeneratedRegex(
        @"\Ahttps?://(?<host>[A-Za-z0-9.-]{1,253})(:[0-9]{1,5})?([/?#][A-Za-z0-9._~/%?&=#+:@,-]*)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUrlShape();

    /// <summary>Whether text is an acceptable redirect path.</summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>True when it is rooted, bounded, free of empty or dot segments and clear of the ACME challenge path.</returns>
    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path)
            || path.Length > SiteOptionsLimits.MaximumRedirectPathLength
            || !PathShape().IsMatch(path))
        {
            return false;
        }

        // "//" is an empty segment, and "." / ".." are segments a web server normalises: a rule written
        // with one would not match the request it reads as matching.
        var segments = path.Split('/');
        for (var index = 1; index < segments.Length; index++)
        {
            var isLast = index == segments.Length - 1;
            if (segments[index] is "." or ".." || (segments[index].Length == 0 && !isLast))
            {
                return false;
            }
        }

        return !path.StartsWith(AcmeChallengePrefix, StringComparison.Ordinal);
    }

    /// <summary>Whether text is an acceptable redirect target.</summary>
    /// <param name="target">The candidate target.</param>
    /// <returns>True for an absolute http(s) URL with a well-formed host, or for a path on the same site.</returns>
    public static bool IsValidTarget(string? target)
    {
        if (string.IsNullOrEmpty(target) || target.Length > SiteOptionsLimits.MaximumRedirectTargetLength)
        {
            return false;
        }

        var absolute = AbsoluteUrlShape().Match(target);
        if (absolute.Success)
        {
            return HostNameRule.IsHostName(absolute.Groups["host"].Value);
        }

        // A site-relative target is a path, never a protocol-relative "//host" (which a browser reads
        // as another site) and never one that already carries its own query.
        return !target.StartsWith("//", StringComparison.Ordinal) && IsValidPath(target);
    }

    /// <summary>Whether a prefix rule's path is well formed: it must end in "/" so it names a directory.</summary>
    /// <param name="path">A path <see cref="IsValidPath"/> accepted.</param>
    /// <returns>True when the path ends in "/".</returns>
    public static bool IsValidPrefix(string path)
    {
        return path.EndsWith('/');
    }
}
