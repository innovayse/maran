namespace Maran.SharedKernel.Utilities.Versions;

/// <summary>
/// The versions a dependency accepts, written as one or more comparators joined by single spaces —
/// <c>&gt;=1.2.0 &lt;2.0.0</c> — and the question "does this version satisfy it".
/// </summary>
/// <remarks>
/// <para>
/// A comparator is an operator (<c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c> or <c>=</c>) followed
/// by a <see cref="SemanticVersion"/>; a bare version means <c>=</c>. A version satisfies the range when
/// it satisfies EVERY comparator. That is the whole grammar: there is no <c>||</c>, no <c>^</c>, no
/// <c>~</c> and no wildcard, because a range an administrator reads before installing a package must
/// mean exactly what it says. A range that accepts every release is written <c>&gt;=0.0.0</c>.
/// </para>
/// <para>
/// A pre-release version satisfies a range only when some comparator names a pre-release of the SAME
/// <c>major.minor.patch</c>: <c>&gt;=1.2.0</c> does not admit <c>1.3.0-beta.1</c>, so a dependency on
/// a stable line is never silently satisfied by an unfinished build.
/// </para>
/// <para>
/// Parsing is strict and never throws: text longer than <see cref="MaxLength"/>, more than
/// <see cref="MaxComparators"/> comparators, an empty comparator (two spaces, a leading or trailing
/// space) and a comparator whose version is not a valid <see cref="SemanticVersion"/> are refused.
/// </para>
/// </remarks>
public sealed class VersionRange
{
    /// <summary>The longest text <see cref="TryParse"/> reads; anything longer is refused unread.</summary>
    public const int MaxLength = 256;

    /// <summary>The most comparators a range may hold.</summary>
    public const int MaxComparators = 8;

    /// <summary>The operators, two-character ones first so <c>&gt;=</c> is not read as <c>&gt;</c>.</summary>
    private static readonly string[] Operators = [">=", "<=", ">", "<", "="];

    /// <summary>The comparators, every one of which a version must satisfy.</summary>
    private readonly (string Operator, SemanticVersion Version)[] _comparators;

    /// <summary>Builds a range from comparators <see cref="TryParse"/> has already validated.</summary>
    /// <param name="comparators">The validated comparators; never empty.</param>
    private VersionRange((string Operator, SemanticVersion Version)[] comparators)
    {
        _comparators = comparators;
    }

    /// <summary>Parses a range. Never throws.</summary>
    /// <param name="text">The text to read; null or anything unreadable is refused.</param>
    /// <param name="range">The parsed range; null when the text was refused.</param>
    /// <returns>True when the text is a strictly valid range.</returns>
    public static bool TryParse(string? text, out VersionRange? range)
    {
        range = null;

        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var parts = text.Split(' ');

        if (parts.Length > MaxComparators)
        {
            return false;
        }

        var comparators = new (string Operator, SemanticVersion Version)[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseComparator(parts[i], out var comparator))
            {
                return false;
            }

            comparators[i] = comparator;
        }

        range = new VersionRange(comparators);
        return true;
    }

    /// <summary>True when the version satisfies every comparator, under the pre-release rule above.</summary>
    /// <param name="version">The version to test.</param>
    /// <returns>True when the version is inside the range.</returns>
    public bool Contains(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.IsPrerelease && !_comparators.Any(c =>
            {
                return NamesPreReleaseOfSameCore(c.Version, version);
            }))
        {
            return false;
        }

        return _comparators.All(c =>
        {
            return Satisfies(c.Operator, c.Version, version);
        });
    }

    /// <summary>The range in its canonical text, every comparator with its operator spelled out.</summary>
    /// <returns>The text <see cref="TryParse"/> reads back to an equal range.</returns>
    public override string ToString()
    {
        return string.Join(' ', _comparators.Select(c =>
        {
            return c.Operator + c.Version;
        }));
    }

    /// <summary>Reads one comparator: an optional operator, then a version.</summary>
    /// <param name="text">The comparator text.</param>
    /// <param name="comparator">The comparator read; default when refused.</param>
    /// <returns>True when the text is a valid comparator.</returns>
    private static bool TryParseComparator(string text, out (string Operator, SemanticVersion Version) comparator)
    {
        comparator = default;
        var op = "=";
        var versionText = text;

        foreach (var candidate in Operators)
        {
            if (text.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                versionText = text.Substring(candidate.Length);
                break;
            }
        }

        if (!SemanticVersion.TryParse(versionText, out var version))
        {
            return false;
        }

        comparator = (op, version!);
        return true;
    }

    /// <summary>True when the bound is a pre-release of the same <c>major.minor.patch</c> as the candidate.</summary>
    /// <param name="bound">A comparator's version.</param>
    /// <param name="candidate">The pre-release version being tested.</param>
    /// <returns>True when the pre-release rule admits the candidate on this bound's account.</returns>
    private static bool NamesPreReleaseOfSameCore(SemanticVersion bound, SemanticVersion candidate)
    {
        return bound.IsPrerelease
            && bound.Major == candidate.Major
            && bound.Minor == candidate.Minor
            && bound.Patch == candidate.Patch;
    }

    /// <summary>Applies one operator to a candidate and a bound.</summary>
    /// <param name="op">One of <see cref="Operators"/>.</param>
    /// <param name="bound">The comparator's version.</param>
    /// <param name="candidate">The version being tested.</param>
    /// <returns>True when the candidate satisfies the comparator.</returns>
    private static bool Satisfies(string op, SemanticVersion bound, SemanticVersion candidate)
    {
        var order = SemanticVersion.Compare(candidate, bound);

        return op switch
        {
            ">=" => order >= 0,
            ">" => order > 0,
            "<=" => order <= 0,
            "<" => order < 0,
            _ => order == 0,
        };
    }
}
