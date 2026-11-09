using System.Globalization;
using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// Counts, per module id, the SPA routes that declare the module in their <c>meta</c>, and holds the
/// counts of the packageable modules against a baseline that may only fall.
/// </summary>
/// <remarks>
/// <para>
/// The SPA must not hard-code what a packageable module's manifest owns (issue #182 item 6). It still
/// does for every module today, so the gate cannot yet say "none"; it says "no more than before", and
/// the baseline file is the ratchet. This is an architecture TEST reading the router as text, not a
/// check in <c>maran structure</c>: the module kinds are C# data, and a shell script reading C# would
/// be a second, divergent parser of them.
/// </para>
/// <para>
/// UNOBSERVED HERE: routes declared outside <c>router/index.ts</c> (<c>navigationTree.ts</c> and
/// <c>moduleLandingRoute.ts</c> also name modules), and a route whose <c>meta</c> is built by a
/// function. The census counts the literal <c>module: '&lt;id&gt;'</c> spelling the router uses.
/// </para>
/// </remarks>
public static class PackageableSpaRouteCensus
{
    /// <summary>Matches the module a route's <c>meta</c> names.</summary>
    private static readonly Regex ModuleMeta =
        new(@"module:\s*'(?<id>[a-z0-9]+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Counts the routes naming each module.</summary>
    /// <param name="routerText">The text of the router file.</param>
    /// <returns>Module id to the number of <c>module: '&lt;id&gt;'</c> occurrences.</returns>
    public static IReadOnlyDictionary<string, int> CountsOf(string routerText)
    {
        return ModuleMeta.Matches(routerText)
            .Select(match => { return match.Groups["id"].Value; })
            .GroupBy(id => { return id; }, StringComparer.Ordinal)
            .ToDictionary(group => { return group.Key; }, group => { return group.Count(); }, StringComparer.Ordinal);
    }

    /// <summary>Reads a baseline file's text.</summary>
    /// <param name="text">The text of <c>PackageableSpaRouteBaseline.txt</c>.</param>
    /// <returns>Module id to its recorded count.</returns>
    /// <exception cref="FormatException">A line is not <c>id|count</c>.</exception>
    public static IReadOnlyDictionary<string, int> ParseBaseline(string text)
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length != 2
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw new FormatException($"Baseline line '{line}' is not '<module id>|<count>'.");
            }

            baseline[parts[0].Trim()] = count;
        }

        return baseline;
    }

    /// <summary>Holds the packageable modules' counts against the baseline.</summary>
    /// <param name="counts">What the router holds now.</param>
    /// <param name="baseline">What the baseline file records.</param>
    /// <param name="packageableIds">The ids of the packageable modules.</param>
    /// <returns>One line per module whose count rose, and one per count that fell without the baseline following.</returns>
    public static IReadOnlyList<string> Violations(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyDictionary<string, int> baseline,
        IEnumerable<string> packageableIds)
    {
        var found = new List<string>();
        foreach (var id in packageableIds.Order(StringComparer.Ordinal))
        {
            var now = counts.GetValueOrDefault(id);
            var recorded = baseline.GetValueOrDefault(id);

            if (now > recorded)
            {
                found.Add(
                    $"Module '{id}' is packageable and the router declares it {now.ToString(CultureInfo.InvariantCulture)} "
                    + $"times, above the recorded {recorded.ToString(CultureInfo.InvariantCulture)}. Its menu and routes "
                    + "come from its manifest, not from the SPA.");
            }
            else if (now < recorded)
            {
                found.Add(
                    $"Module '{id}' is declared {now.ToString(CultureInfo.InvariantCulture)} times by the router, below "
                    + $"the recorded {recorded.ToString(CultureInfo.InvariantCulture)}. Lower the number in "
                    + "PackageableSpaRouteBaseline.txt so the ratchet follows.");
            }
        }

        return found;
    }
}
