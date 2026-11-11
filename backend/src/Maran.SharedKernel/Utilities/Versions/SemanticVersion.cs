using System.Globalization;

namespace Maran.SharedKernel.Utilities.Versions;

/// <summary>
/// A version written under the Semantic Versioning 2.0.0 grammar — <c>major.minor.patch</c>, an
/// optional <c>-pre.release</c> and an optional <c>+build</c> — parsed strictly, and the ordering the
/// specification defines.
/// </summary>
/// <remarks>
/// <para>
/// A wrong comparison here decides whether a module built for a newer panel is allowed to compose, so
/// parsing refuses everything it cannot read exactly: surrounding whitespace, a leading <c>v</c>, a
/// missing component, a number with a leading zero or too large for an <see cref="int"/>, an empty
/// identifier, and any character outside ASCII letters, digits and hyphen in a suffix. Text longer
/// than <see cref="MaxLength"/> is refused before it is read at all. <see cref="TryParse"/> never
/// throws.
/// </para>
/// <para>
/// Ordering follows the specification: the three numbers first; a version with a pre-release part is
/// LOWER than the same numbers without one; pre-release identifiers compare left to right, a numeric
/// identifier is lower than an alphanumeric one, two numeric ones compare by value and two
/// alphanumeric ones in ordinal order, and when all shared identifiers are equal the version with
/// fewer identifiers is lower. The build metadata takes no part: two versions that differ only in
/// it are equal, which is why <see cref="Equals(SemanticVersion)"/> is not a comparison of the text.
/// </para>
/// </remarks>
public sealed class SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>
{
    /// <summary>The longest text <see cref="TryParse"/> reads; anything longer is refused unread.</summary>
    public const int MaxLength = 128;

    /// <summary>The pre-release identifiers, in order; empty for a release.</summary>
    private readonly string[] _preReleaseIdentifiers;

    /// <summary>The major number.</summary>
    public int Major { get; }

    /// <summary>The minor number.</summary>
    public int Minor { get; }

    /// <summary>The patch number.</summary>
    public int Patch { get; }

    /// <summary>The pre-release part without its leading hyphen, or the empty string for a release.</summary>
    public string PreRelease { get; }

    /// <summary>The build metadata without its leading plus sign, or the empty string when there is none.</summary>
    public string Build { get; }

    /// <summary>True when the version has a pre-release part, and therefore sorts below its release.</summary>
    public bool IsPrerelease
    {
        get { return _preReleaseIdentifiers.Length > 0; }
    }

    /// <summary>Builds a version from parts <see cref="TryParse"/> has already validated.</summary>
    /// <param name="major">The major number.</param>
    /// <param name="minor">The minor number.</param>
    /// <param name="patch">The patch number.</param>
    /// <param name="preRelease">The pre-release part, or the empty string.</param>
    /// <param name="build">The build metadata, or the empty string.</param>
    private SemanticVersion(int major, int minor, int patch, string preRelease, string build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        Build = build;
        _preReleaseIdentifiers = preRelease.Length == 0 ? Array.Empty<string>() : preRelease.Split('.');
    }

    /// <summary>Parses a version. Never throws.</summary>
    /// <param name="text">The text to read; null or anything unreadable is refused.</param>
    /// <param name="version">The parsed version; null when the text was refused.</param>
    /// <returns>True when the text is a strictly valid semantic version.</returns>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;

        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var build = string.Empty;
        var withoutBuild = text;
        var plus = text.IndexOf('+');

        if (plus >= 0)
        {
            build = text.Substring(plus + 1);
            withoutBuild = text.Substring(0, plus);

            if (!AreIdentifiers(build, rejectLeadingZeros: false))
            {
                return false;
            }
        }

        var preRelease = string.Empty;
        var core = withoutBuild;
        var hyphen = withoutBuild.IndexOf('-');

        if (hyphen >= 0)
        {
            preRelease = withoutBuild.Substring(hyphen + 1);
            core = withoutBuild.Substring(0, hyphen);

            if (!AreIdentifiers(preRelease, rejectLeadingZeros: true))
            {
                return false;
            }
        }

        var numbers = core.Split('.');

        if (numbers.Length != 3
            || !TryParseNumber(numbers[0], out var major)
            || !TryParseNumber(numbers[1], out var minor)
            || !TryParseNumber(numbers[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease, build);
        return true;
    }

    /// <summary>Orders two versions by the Semantic Versioning 2.0.0 precedence rules.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Negative when <paramref name="left"/> is lower, zero when equal, positive when higher.</returns>
    public static int Compare(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var byNumbers = left.Major.CompareTo(right.Major);

        if (byNumbers == 0)
        {
            byNumbers = left.Minor.CompareTo(right.Minor);
        }

        if (byNumbers == 0)
        {
            byNumbers = left.Patch.CompareTo(right.Patch);
        }

        if (byNumbers != 0)
        {
            return byNumbers;
        }

        if (!left.IsPrerelease || !right.IsPrerelease)
        {
            return right.IsPrerelease.CompareTo(left.IsPrerelease);
        }

        return ComparePreRelease(left._preReleaseIdentifiers, right._preReleaseIdentifiers);
    }

    /// <summary>Orders this version against another.</summary>
    /// <param name="other">The version to compare with; null orders below every version.</param>
    /// <returns>Negative when this version is lower, zero when equal, positive when higher.</returns>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        return Compare(this, other);
    }

    /// <summary>True when the other version has the same precedence; the build metadata is ignored.</summary>
    /// <param name="other">The version to compare with, or null.</param>
    /// <returns>True when both versions order as equal.</returns>
    public bool Equals(SemanticVersion? other)
    {
        return other is not null && Compare(this, other) == 0;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as SemanticVersion);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch, PreRelease);
    }

    /// <summary>True when both versions have the same precedence, or both are null; the build metadata is ignored.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when the versions order as equal.</returns>
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    /// <summary>True when the versions differ in precedence.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when the versions are not equal.</returns>
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right)
    {
        return !(left == right);
    }

    /// <summary>True when the first version has lower precedence; null orders below every version.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when <paramref name="left"/> is lower.</returns>
    public static bool operator <(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null)
        {
            return right is not null;
        }

        return left.CompareTo(right) < 0;
    }

    /// <summary>True when the first version is lower or equal; null orders below every version.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when <paramref name="left"/> is not higher.</returns>
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null)
        {
            return true;
        }

        return left.CompareTo(right) <= 0;
    }

    /// <summary>True when the first version has higher precedence; null orders below every version.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when <paramref name="left"/> is higher.</returns>
    public static bool operator >(SemanticVersion? left, SemanticVersion? right)
    {
        return left is not null && left.CompareTo(right) > 0;
    }

    /// <summary>True when the first version is higher or equal; null orders below every version.</summary>
    /// <param name="left">The first version, or null.</param>
    /// <param name="right">The second version, or null.</param>
    /// <returns>True when <paramref name="left"/> is not lower.</returns>
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.CompareTo(right) >= 0;
    }

    /// <summary>The version in its canonical text, including any pre-release and build parts.</summary>
    /// <returns>The text <see cref="TryParse"/> reads back to an equal version.</returns>
    public override string ToString()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

        if (PreRelease.Length > 0)
        {
            text += "-" + PreRelease;
        }

        if (Build.Length > 0)
        {
            text += "+" + Build;
        }

        return text;
    }

    /// <summary>Compares two non-empty pre-release identifier lists by the specification's rules.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns>Negative, zero or positive, as for <see cref="Compare"/>.</returns>
    private static int ComparePreRelease(string[] left, string[] right)
    {
        var shared = Math.Min(left.Length, right.Length);

        for (var i = 0; i < shared; i++)
        {
            var byIdentifier = CompareIdentifier(left[i], right[i]);

            if (byIdentifier != 0)
            {
                return byIdentifier;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>Compares two pre-release identifiers: numeric below alphanumeric, numbers by value.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns>Negative, zero or positive.</returns>
    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);

        if (leftNumeric && rightNumeric)
        {
            // Identifiers carry no leading zero, so a longer one is a larger number; comparing as
            // text after the length keeps an identifier of any size from overflowing an integer.
            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    /// <summary>True when every character of a non-empty identifier is an ASCII digit.</summary>
    /// <param name="identifier">The identifier to test.</param>
    /// <returns>True for an all-digit identifier.</returns>
    private static bool IsNumeric(string identifier)
    {
        return identifier.Length > 0 && identifier.All(char.IsAsciiDigit);
    }

    /// <summary>Reads one of the three core numbers: plain digits, no leading zero, within an int.</summary>
    /// <param name="text">The component text.</param>
    /// <param name="number">The value read; zero when refused.</param>
    /// <returns>True when the component is a valid number.</returns>
    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;

        if (text.Length == 0 || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// True when a dot-separated suffix is a non-empty list of non-empty identifiers made of ASCII
    /// letters, digits and hyphens.
    /// </summary>
    /// <param name="suffix">The pre-release or build text, without its leading sign.</param>
    /// <param name="rejectLeadingZeros">True for a pre-release, where a numeric identifier has no leading zero.</param>
    /// <returns>True when the suffix is valid.</returns>
    private static bool AreIdentifiers(string suffix, bool rejectLeadingZeros)
    {
        if (suffix.Length == 0)
        {
            return false;
        }

        foreach (var identifier in suffix.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(c =>
                {
                    return char.IsAsciiLetterOrDigit(c) || c == '-';
                }))
            {
                return false;
            }

            if (rejectLeadingZeros && IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }
}
