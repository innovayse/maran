namespace Maran.ArchitectureTests;

/// <summary>
/// Compares what the Sdk and the modules do now with what the committed record says. Pure: it reads
/// two in-memory values, so a control can hand it a planted one.
/// </summary>
public static class SdkSurfaceComparison
{
    /// <summary>Lines to paste into the record to accept a changed surface; empty when it is exact.</summary>
    /// <param name="recorded">The recorded surface.</param>
    /// <param name="actual">The surface the Sdk exports now.</param>
    /// <returns>One <c>add type …</c> or <c>remove type …</c> line per difference.</returns>
    public static List<string> SurfaceDifferences(IReadOnlySet<string> recorded, IReadOnlyCollection<string> actual)
    {
        var differences = new List<string>();
        differences.AddRange(actual
            .Where(name =>
            {
                return !recorded.Contains(name);
            })
            .OrderBy(name =>
            {
                return name;
            }, StringComparer.Ordinal)
            .Select(name =>
            {
                return $"add    type {name}";
            }));
        differences.AddRange(recorded
            .Where(name =>
            {
                return !actual.Contains(name);
            })
            .OrderBy(name =>
            {
                return name;
            }, StringComparer.Ordinal)
            .Select(name =>
            {
                return $"remove type {name}";
            }));
        return differences;
    }

    /// <summary>The modules that use more Sdk types than their recorded ceiling, or have none.</summary>
    /// <param name="ceilings">The recorded ceilings by module name.</param>
    /// <param name="usage">How many Sdk types each module uses now.</param>
    /// <returns>One sentence per module over its ceiling or missing a row, naming the line to record.</returns>
    public static List<string> CeilingBreaches(
        IReadOnlyDictionary<string, int> ceilings,
        IReadOnlyDictionary<string, int> usage)
    {
        var breaches = new List<string>();
        foreach (var (module, used) in usage.OrderBy(
            pair =>
            {
                return pair.Key;
            },
            StringComparer.Ordinal))
        {
            if (!ceilings.TryGetValue(module, out var ceiling))
            {
                breaches.Add($"{module} has no recorded ceiling; it uses {used} Sdk types: 'module {module} {used}'.");
            }
            else if (used > ceiling)
            {
                breaches.Add($"{module} uses {used} Sdk types, past its ceiling of {ceiling}: 'module {module} {used}'.");
            }
        }

        return breaches;
    }

    /// <summary>The recorded rows that no longer match a module, or sit above what the module uses.</summary>
    /// <param name="ceilings">The recorded ceilings by module name.</param>
    /// <param name="usage">How many Sdk types each module uses now.</param>
    /// <returns>One hint per row that can be lowered or deleted.</returns>
    public static List<string> LoweringHints(
        IReadOnlyDictionary<string, int> ceilings,
        IReadOnlyDictionary<string, int> usage)
    {
        var hints = new List<string>();
        foreach (var (module, ceiling) in ceilings.OrderBy(
            pair =>
            {
                return pair.Key;
            },
            StringComparer.Ordinal))
        {
            if (!usage.TryGetValue(module, out var used))
            {
                hints.Add($"remove 'module {module} {ceiling}': no such module is loaded.");
            }
            else if (used < ceiling)
            {
                hints.Add($"lower 'module {module} {ceiling}' to 'module {module} {used}'.");
            }
        }

        return hints;
    }
}
