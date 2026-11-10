using Maran.Host.Modules;
using Maran.Sdk.Interfaces;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every word a module's manifest asks the catalogue to show — its name, its menu labels, its route
/// titles — has a value in English, Russian and Armenian in the module's own <c>DisplayNames</c> triple.
/// </summary>
/// <remarks>
/// <para>
/// This suite extends <see cref="ResourceKeyParityTests"/> and <see cref="ModuleDisplayNameLawTests"/>
/// to the keys the contract adds (see <see cref="ModuleResourceLaw"/> for what they already hold and
/// what this adds), and reuses <see cref="ResourceTriples"/> to read the authored files. It does not
/// re-implement either.
/// </para>
/// <para>
/// The census is guarded on the axis that can go blind: the keys examined must be non-empty, and a
/// module's own triple must exist, because <see cref="ResourceTriples.Read(string)"/> answers a
/// missing file with an empty map and an empty map would otherwise turn "no file" into "no keys to
/// check".
/// </para>
/// </remarks>
public sealed class ModuleResourceContractTests
{
    /// <summary>The module folder prefix of the assembly names.</summary>
    private const string AssemblyPrefix = "Maran.Modules.";

    /// <summary>Every module's catalogue words exist in all three languages.</summary>
    [Fact]
    public void Every_modules_catalogue_words_exist_in_english_russian_and_armenian()
    {
        var subjects = RealSubjects();

        var violations = ModuleResourceLaw.Violations(subjects);

        Assert.True(
            violations.Count == 0,
            "A module's manifest asks for words its DisplayNames triple does not hold in every language:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>The census read the keys it claims to judge, and every module has its triple.</summary>
    [Fact]
    public void The_census_read_the_keys_and_every_module_has_its_triple()
    {
        var subjects = RealSubjects();

        Assert.NotEmpty(subjects);
        Assert.All(subjects, subject =>
        {
            Assert.NotEmpty(subject.Keys);
            Assert.NotEmpty(subject.English);
            Assert.NotEmpty(subject.Russian);
            Assert.NotEmpty(subject.Armenian);
        });
    }

    /// <summary>A key missing in one language is flagged, and the same key present in all three is accepted.</summary>
    [Fact]
    public void A_planted_key_missing_in_one_language_is_flagged_and_a_complete_one_is_accepted()
    {
        var complete = Subject("widgets", ["WidgetsMenu"], english: "Widgets", russian: "ru Widgets", armenian: "hy Widgets");
        var noRussian = complete with { Russian = new Dictionary<string, string>() };

        Assert.Empty(ModuleResourceLaw.Violations([complete]));
        Assert.Contains(
            ModuleResourceLaw.Violations([noRussian]),
            line => { return line.StartsWith(ModuleResourceLaw.ValueMissing, StringComparison.Ordinal) && line.Contains("russian", StringComparison.Ordinal); });
    }

    /// <summary>A value that is the key itself is flagged.</summary>
    [Fact]
    public void A_planted_value_that_is_the_key_itself_is_flagged()
    {
        var subject = Subject("widgets", ["WidgetsMenu"], english: "WidgetsMenu", russian: "ru Widgets", armenian: "hy Widgets");

        Assert.Contains(
            ModuleResourceLaw.Violations([subject]),
            line => { return line.StartsWith(ModuleResourceLaw.ValueIsKey, StringComparison.Ordinal); });
    }

    /// <summary>A key declared by two modules is flagged, and distinct keys are accepted.</summary>
    [Fact]
    public void A_planted_key_shared_by_two_modules_is_flagged_and_distinct_keys_are_accepted()
    {
        var first = Subject("widgets", ["SharedLabel"], english: "A", russian: "ru A", armenian: "hy A");
        var second = Subject("gadgets", ["SharedLabel"], english: "D", russian: "ru D", armenian: "hy D");
        var distinct = Subject("gadgets", ["GadgetsLabel"], english: "D", russian: "ru D", armenian: "hy D");

        Assert.Contains(
            ModuleResourceLaw.Violations([first, second]),
            line => { return line.StartsWith(ModuleResourceLaw.KeyShared, StringComparison.Ordinal); });
        Assert.Empty(ModuleResourceLaw.Violations([first, distinct]));
    }

    /// <summary>Builds a planted row whose three languages each hold the given value for every key.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="keys">The keys the module declares.</param>
    /// <param name="english">The English value of every key.</param>
    /// <param name="russian">The Russian value of every key.</param>
    /// <param name="armenian">The Armenian value of every key.</param>
    /// <returns>The planted row.</returns>
    private static ModuleResourceSubject Subject(
        string moduleId,
        string[] keys,
        string english,
        string russian,
        string armenian)
    {
        return new ModuleResourceSubject(
            moduleId,
            keys,
            keys.ToDictionary(key => { return key; }, _ => { return english; }, StringComparer.Ordinal),
            keys.ToDictionary(key => { return key; }, _ => { return russian; }, StringComparer.Ordinal),
            keys.ToDictionary(key => { return key; }, _ => { return armenian; }, StringComparer.Ordinal));
    }

    /// <summary>Reads every composed module's declared keys and its triple's entries.</summary>
    /// <returns>One row per module, Licensing included.</returns>
    private static List<ModuleResourceSubject> RealSubjects()
    {
        return ModuleRegistry.All
            .Select(SubjectOf)
            .ToList();
    }

    /// <summary>Reads one module.</summary>
    /// <param name="module">The module to read.</param>
    /// <returns>Its keys and the entries of its <c>DisplayNames</c> triple.</returns>
    private static ModuleResourceSubject SubjectOf(IPanelModule module)
    {
        var manifest = module.Manifest;
        var folderName = module.GetType().Assembly.GetName().Name![AssemblyPrefix.Length..];
        var neutral = ResourceTriples.NeutralPathOf($"Maran.Modules/{folderName}/Resources/DisplayNames.resx");

        var keys = new List<string> { manifest.DisplayNameKey };
        keys.AddRange(manifest.Menu.Select(item => { return item.LabelKey; }));
        keys.AddRange(manifest.Routes.Select(route => { return route.TitleKey; }));

        return new ModuleResourceSubject(
            manifest.Id,
            keys,
            ResourceTriples.Read(neutral),
            ResourceTriples.Read(ResourceTriples.TranslatedPathOf(neutral, "ru")),
            ResourceTriples.Read(ResourceTriples.TranslatedPathOf(neutral, "hy")));
    }
}
