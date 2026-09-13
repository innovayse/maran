namespace Maran.ArchitectureTests;

/// <summary>
/// The rule <see cref="ModuleDisplayNameLawTests"/> enforces: the name every screen shows for a
/// module is a name a reader of that language would recognise, and it is that module's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a law over the VALUES at all.</b> Two suites already read these keys and neither reads
/// what they hold: <see cref="ManifestUniquenessTests"/> checks that the key exists somewhere and
/// starts with the module's id, and <see cref="ResourceKeyParityTests"/> compares key SETS across
/// the triple. So a <c>.ru</c> entry holding the English words, or holding another module's
/// translation, passes every gate this repository has and reaches a customer. The SPA cannot close
/// it either — every Playwright spec stubs the modules response, so an assertion on the sidebar
/// asserts its own fixture.
/// </para>
/// <para>
/// <b>The property, and why it is this one.</b> Asserting exact translated strings would turn every
/// copy edit into a failing test, which teaches contributors to update tests reflexively — the habit
/// that makes the next real failure look like noise. Asserting merely non-empty observes almost
/// nothing, because the failure mode is a value that IS present. What holds without pinning wording
/// is: a translated name exists, is not the resource key, is not the neutral English text, and is
/// not any other module's name in the same language. The last clause is the one with teeth — the
/// realistic wrong translation here is not invented prose, it is the line above or below pasted into
/// the wrong file, and that is exactly what a same-language collision is.
/// </para>
/// <para>
/// <b>The declared out.</b> A module whose name is a protocol keeps the same word in every language
/// — the Ftp module is <c>FTPS</c> in all three, as SFTP and SSL are — so identity with the English
/// value is legitimate there and nowhere else. It is declared per module with its reason, and an
/// excuse for a module that no longer needs it fails, so the register cannot rot into a place a
/// genuine untranslated name hides.
/// </para>
/// <para>
/// UNOBSERVED HERE: whether a translation MEANS what the English does. A fluent Armenian sentence
/// naming the wrong thing satisfies every clause above. No gate in this repository can see that;
/// what this law removes is the far larger class where nobody translated at all, or translated into
/// the neighbouring module's words.
/// </para>
/// </remarks>
public static class ModuleDisplayNameLaw
{
    /// <summary>Names every way a set of module display names breaks the law.</summary>
    /// <param name="names">One row per module, as the resource files hold it.</param>
    /// <param name="untranslatedByDesign">
    /// Module id to the reason its name is deliberately the same word in every language.
    /// </param>
    /// <returns>The violations, ordered; empty when the set obeys the law.</returns>
    public static IReadOnlyList<string> Violations(
        IReadOnlyList<ModuleDisplayName> names,
        IReadOnlyDictionary<string, string> untranslatedByDesign)
    {
        var problems = new List<string>();
        foreach (var name in names)
        {
            problems.AddRange(ViolationsOf(name, untranslatedByDesign.ContainsKey(name.ModuleId)));
        }

        problems.AddRange(Collisions(names, "english", name =>
        {
            return name.English;
        }));
        problems.AddRange(Collisions(names, "russian", name =>
        {
            return name.Russian;
        }));
        problems.AddRange(Collisions(names, "armenian", name =>
        {
            return name.Armenian;
        }));
        problems.AddRange(StaleExcuses(names, untranslatedByDesign));

        return problems.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Names the ways one module's row breaks the law.</summary>
    /// <param name="name">The row to judge.</param>
    /// <param name="excused">Whether this module's name is the same word in every language by design.</param>
    /// <returns>The violations for this row.</returns>
    private static List<string> ViolationsOf(ModuleDisplayName name, bool excused)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name.English))
        {
            problems.Add($"{name.ModuleId}: '{name.Key}' has no english value, so the catalogue shows the key.");
            return problems;
        }

        if (string.Equals(name.English, name.Key, StringComparison.Ordinal))
        {
            problems.Add($"{name.ModuleId}: the english name IS the resource key '{name.Key}'.");
        }

        problems.AddRange(TranslationViolations(name, "russian", name.Russian, excused));
        problems.AddRange(TranslationViolations(name, "armenian", name.Armenian, excused));
        return problems;
    }

    /// <summary>Names the ways one translated value of one module breaks the law.</summary>
    /// <param name="name">The row the value belongs to.</param>
    /// <param name="language">The language, as a failure message names it.</param>
    /// <param name="value">The translated value, or <c>null</c> when the file declares no entry.</param>
    /// <param name="excused">Whether this module's name is the same word in every language by design.</param>
    /// <returns>The violations for this value.</returns>
    private static List<string> TranslationViolations(
        ModuleDisplayName name,
        string language,
        string? value,
        bool excused)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add(
                $"{name.ModuleId}: '{name.Key}' has no {language} value, and a missing translated "
                + "entry does not fail — it silently serves the english text.");
            return problems;
        }

        if (string.Equals(value, name.Key, StringComparison.Ordinal))
        {
            problems.Add($"{name.ModuleId}: the {language} name IS the resource key '{name.Key}'.");
            return problems;
        }

        if (!excused && string.Equals(value, name.English, StringComparison.Ordinal))
        {
            problems.Add(
                $"{name.ModuleId}: the {language} name is the english '{name.English}'. If that is "
                + "right because the name is a protocol, declare it with its reason.");
        }

        return problems;
    }

    /// <summary>Names every pair of modules sharing one name in one language.</summary>
    /// <param name="names">The rows to judge.</param>
    /// <param name="language">The language, as a failure message names it.</param>
    /// <param name="valueOf">Reads that language's value from a row.</param>
    /// <returns>One violation per colliding value.</returns>
    private static List<string> Collisions(
        IReadOnlyList<ModuleDisplayName> names,
        string language,
        Func<ModuleDisplayName, string?> valueOf)
    {
        return names
            .Where(name =>
            {
                return !string.IsNullOrWhiteSpace(valueOf(name));
            })
            .GroupBy(
                name =>
                {
                    return valueOf(name)!;
                },
                StringComparer.Ordinal)
            .Where(group =>
            {
                return group.Count() > 1;
            })
            .Select(group =>
            {
                var modules = group.Select(name =>
                {
                    return name.ModuleId;
                }).Order(StringComparer.Ordinal);
                return $"{string.Join(" and ", modules)} are both named '{group.Key}' in {language}; "
                    + "one of them is showing the other's words.";
            })
            .ToList();
    }

    /// <summary>Names every excuse that no longer describes the module it was written for.</summary>
    /// <param name="names">The rows to judge.</param>
    /// <param name="untranslatedByDesign">Module id to the reason its name is one word everywhere.</param>
    /// <returns>One violation per stale excuse.</returns>
    private static List<string> StaleExcuses(
        IReadOnlyList<ModuleDisplayName> names,
        IReadOnlyDictionary<string, string> untranslatedByDesign)
    {
        var byModule = names.ToDictionary(
            name =>
            {
                return name.ModuleId;
            },
            name =>
            {
                return name;
            },
            StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (moduleId, reason) in untranslatedByDesign)
        {
            if (!byModule.TryGetValue(moduleId, out var name))
            {
                problems.Add($"'{moduleId}' is excused from translation ({reason}) and is not a module.");
                continue;
            }

            var identical = string.Equals(name.Russian, name.English, StringComparison.Ordinal)
                && string.Equals(name.Armenian, name.English, StringComparison.Ordinal);
            if (!identical)
            {
                problems.Add(
                    $"'{moduleId}' is excused from translation ({reason}) but its name is now "
                    + "translated; the excuse is a hole where an untranslated name could hide.");
            }
        }

        return problems;
    }
}
