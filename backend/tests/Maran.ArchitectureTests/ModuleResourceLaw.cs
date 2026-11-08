namespace Maran.ArchitectureTests;

/// <summary>
/// The rule <see cref="ModuleResourceContractTests"/> enforces: every word a module's manifest asks
/// the catalogue to show exists in English, Russian and Armenian, and is that module's own.
/// </summary>
/// <remarks>
/// <para>
/// It extends, and does not repeat, what the existing laws already hold. <see cref="ResourceKeyParityTests"/>
/// compares key SETS across a triple; <see cref="ModuleDisplayNameLaw"/> judges the VALUES of the
/// module's name (translated, not the key, not another module's). Neither reads the menu labels and
/// route titles the contract adds, and a label with no Russian entry does not fail anywhere else: a
/// missing translated entry silently serves the English text, and a key with no entry at all serves
/// the key. The files are read, as <see cref="ResourceTriples"/> explains, and not a localizer.
/// </para>
/// <para>
/// A key declared by two modules is flagged because every module's resources feed one pool
/// (<see cref="ManifestUniquenessTests"/>): the second module's text would silently replace the
/// first's on a customer's screen.
/// </para>
/// <para>
/// UNOBSERVED HERE: whether a translation means what the English does; the label's length against the
/// sidebar it must fit; and whether the key is a word at all rather than another key's name.
/// </para>
/// </remarks>
public static class ModuleResourceLaw
{
    /// <summary>A key has no value, or a blank one, in a language.</summary>
    public const string ValueMissing = "ValueMissing";

    /// <summary>A value is the resource key itself, which is what an unresolved key renders as.</summary>
    public const string ValueIsKey = "ValueIsKey";

    /// <summary>Two modules declare the same key.</summary>
    public const string KeyShared = "KeyShared";

    /// <summary>Names every way a set of modules' catalogue words break the contract.</summary>
    /// <param name="modules">One row per module.</param>
    /// <returns>One line per violation, each beginning with the rule's constant.</returns>
    public static IReadOnlyList<string> Violations(IReadOnlyList<ModuleResourceSubject> modules)
    {
        var found = new List<string>();
        foreach (var module in modules)
        {
            foreach (var key in module.Keys.Distinct(StringComparer.Ordinal))
            {
                found.AddRange(ValueViolations(module.ModuleId, key, "english", module.English));
                found.AddRange(ValueViolations(module.ModuleId, key, "russian", module.Russian));
                found.AddRange(ValueViolations(module.ModuleId, key, "armenian", module.Armenian));
            }
        }

        found.AddRange(modules
            .SelectMany(module =>
            {
                return module.Keys.Distinct(StringComparer.Ordinal).Select(key => { return (key, module.ModuleId); });
            })
            .GroupBy(pair => { return pair.key; }, StringComparer.Ordinal)
            .Where(group => { return group.Count() > 1; })
            .Select(group =>
            {
                return $"{KeyShared}: key '{group.Key}' is declared by "
                    + string.Join(", ", group.Select(pair => { return pair.ModuleId; }).Order(StringComparer.Ordinal))
                    + ", and all modules share one resource pool.";
            }));

        return found.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Judges one key's value in one language.</summary>
    /// <param name="moduleId">The module declaring the key.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="language">The language, as a message names it.</param>
    /// <param name="entries">That language's file, key to value.</param>
    /// <returns>Zero or one violation.</returns>
    private static List<string> ValueViolations(
        string moduleId,
        string key,
        string language,
        IReadOnlyDictionary<string, string> entries)
    {
        if (!entries.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return [$"{ValueMissing}: '{moduleId}' key '{key}' has no {language} value."];
        }

        if (string.Equals(value, key, StringComparison.Ordinal))
        {
            return [$"{ValueIsKey}: '{moduleId}' key '{key}' has the key itself as its {language} value."];
        }

        return [];
    }
}
