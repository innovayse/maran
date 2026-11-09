using System.Globalization;
using System.Text.RegularExpressions;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Updates.Domain.Policies;

/// <summary>
/// What each value root writes into the panel's state files must look like before the panel believes
/// it. Every rule answers "is this plain enough to show or to build a path from", and a value that fails
/// is treated as absent by the caller: never echoed, never thrown (rules/security.md items 1 and 8).
/// </summary>
public static partial class PanelUpdateValueRules
{
    /// <summary>The longest release-notes address that is shown.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>The instant format root writes: UTC, whole seconds, a trailing <c>Z</c>.</summary>
    public const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>The date format of a release's publication day.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The longest release name the installer accepts as a directory name.</summary>
    private const int ReleaseNameMaxLength = 64;

    /// <summary>Whether text is a Semantic Versioning 2.0.0 version, and the parsed version when it is.</summary>
    /// <param name="text">The candidate.</param>
    /// <param name="version">The parsed version.</param>
    /// <returns>True when the text is a version.</returns>
    public static bool TryVersion(string? text, out SemanticVersion? version)
    {
        return SemanticVersion.TryParse(text, out version);
    }

    /// <summary>Whether text names an installed release: a version, or the name a legacy install is adopted under.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>True when it is safe to build a directory name from.</returns>
    public static bool IsReleaseName(string? text)
    {
        if (text is null || text.Length is < 1 or > ReleaseNameMaxLength)
        {
            return false;
        }

        return SemanticVersion.TryParse(text, out _) || LegacyName().IsMatch(text);
    }

    /// <summary>Whether text is a UTC instant in <see cref="InstantFormat"/>.</summary>
    /// <param name="text">The candidate.</param>
    /// <param name="instant">The parsed instant.</param>
    /// <returns>True when the text is exactly that format.</returns>
    public static bool TryInstant(string? text, out DateTimeOffset instant)
    {
        return DateTimeOffset.TryParseExact(
            text,
            InstantFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out instant);
    }

    /// <summary>Whether text is a calendar date in <see cref="DateFormat"/>.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>True when the text is exactly that format.</returns>
    public static bool IsDate(string? text)
    {
        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>Whether text is a plain <c>https</c> address that is safe to show as a link.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>
    /// True for an absolute https address of at most <see cref="MaxUrlLength"/> printable ASCII
    /// characters with no whitespace, control character, quote, angle bracket or backslash.
    /// </returns>
    public static bool IsPlainHttpsUrl(string? text)
    {
        if (text is null || text.Length > MaxUrlLength || !text.StartsWith("https://", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (character <= ' ' || character >= '\u007f' || character is '"' or '\'' or '<' or '>' or '\\')
            {
                return false;
            }
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>Whether text is a release channel the installer knows.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>True for exactly <c>stable</c> or <c>beta</c>.</returns>
    public static bool IsChannel(string? text)
    {
        return text is "stable" or "beta";
    }

    /// <summary>Whether text is a release name safe for a log line and a journal subject.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns>True for 1 to 64 letters, digits, dots, underscores and hyphens.</returns>
    public static bool IsPlainReleaseName(string? text)
    {
        return text is not null && PlainName().IsMatch(text);
    }

    /// <summary>The name a legacy, unversioned install is adopted under.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^legacy-[0-9]{8}T[0-9]{6}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyName();

    /// <summary>The plain release-name alphabet, anchored with <c>\z</c> because <c>$</c> also matches before a final newline.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlainName();
}
