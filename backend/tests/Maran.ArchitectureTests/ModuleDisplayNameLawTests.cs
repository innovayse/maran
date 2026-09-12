using System.Reflection;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every module's name reaches a Russian or Armenian screen in that language, and in its own words.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes was established by inspection before it was written: <c>SftpModuleDisplayName</c>
/// appears in five places in the whole repository — the manifest that names it and the three resource
/// files that hold it — and no test resolves it. <see cref="ManifestUniquenessTests"/> reads the key's
/// existence and its prefix; <see cref="ResourceKeyParityTests"/> reads key sets. Neither reads a
/// value, and the SPA cannot: every Playwright spec stubs <c>GET /api/v1/modules</c>, so a sidebar
/// assertion asserts its own fixture. A wrong translation therefore passed every gate.
/// </para>
/// <para>
/// The property, the reasoning behind choosing it over an exact-string assertion, and what it cannot
/// see are all stated on <see cref="ModuleDisplayNameLaw"/>, because they are properties of the law
/// and not of this suite's plumbing.
/// </para>
/// </remarks>
public sealed class ModuleDisplayNameLawTests
{
    /// <summary>
    /// Modules whose name is deliberately the same word in every language, with the reason.
    /// </summary>
    /// <remarks>
    /// <c>FTPS</c> is a protocol name, written the same in English, Russian and Armenian — the Ftp
    /// module's error messages and the SPA's own file-transfer screen already keep it in Latin script
    /// in all three locales. The staleness clause of the law refuses this entry the moment the name
    /// stops being identical across the triple.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> UntranslatedByDesign =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ftp"] = "FTPS is a protocol name and is written the same in all three languages.",
        };

    /// <summary>Every modules name is translated into russian and armenian and is its own.</summary>
    [Fact]
    public void Every_modules_name_is_translated_into_russian_and_armenian_and_is_its_own()
    {
        var names = AuthoredNames();

        var violations = ModuleDisplayNameLaw.Violations(names, UntranslatedByDesign);

        Assert.True(
            violations.Count == 0,
            "A module's name reaches a screen in the wrong language, or in another module's words "
            + "(rules/architecture.md, 'The backend owns the data, the SPA renders it'):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>The law reads the modules and the resource values it claims to judge.</summary>
    /// <remarks>
    /// The axis that can go blind (rules/testing.md): a registry that failed to compose, or a
    /// resource scan pointed at the wrong root, yields an empty row set and the walk above passes
    /// over nothing. Named modules rather than a count alone, because a partial assembly load leaves
    /// a plausible count behind.
    /// </remarks>
    [Fact]
    public void The_law_reads_the_modules_and_the_resource_values_it_claims_to_judge()
    {
        var names = AuthoredNames();
        var byModule = names.Select(name =>
        {
            return name.ModuleId;
        }).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("sftp", byModule);
        Assert.Contains("ftp", byModule);
        Assert.Contains("identity", byModule);
        Assert.True(
            names.Count >= 13,
            $"Only {names.Count} module manifests were read; the panel ships more, so the assembly "
            + "scan or the resource scan is partial and this suite is reporting on a fraction.");

        // The values themselves were read, not merely the rows: a scan that found every key and no
        // value would satisfy every count above while the law judged a wall of nulls.
        Assert.All(names, name =>
        {
            Assert.False(string.IsNullOrWhiteSpace(name.English));
            Assert.False(string.IsNullOrWhiteSpace(name.Russian));
            Assert.False(string.IsNullOrWhiteSpace(name.Armenian));
        });
    }

    /// <summary>The law accepts a set of names that is right.</summary>
    /// <remarks>
    /// The inverse control (rules/testing.md): a law mutated to refuse everything passes every test
    /// that only ever hands it broken input, and the walk above would then read as a real gate while
    /// the tree was clean for a different reason.
    /// </remarks>
    [Fact]
    public void The_law_accepts_a_set_of_names_that_is_right()
    {
        var clean = new List<ModuleDisplayName>
        {
            new("sites", "SitesModuleDisplayName", "Sites", "Сайты", "Կայքեր"),
            new("cron", "CronModuleDisplayName", "Scheduled tasks", "Запланированные задачи", "Ժամանակացույց"),
        };

        Assert.Empty(ModuleDisplayNameLaw.Violations(clean, NoExcuses()));
    }

    /// <summary>The law refuses a name left in english in a translated file.</summary>
    /// <remarks>
    /// The defect a missing <c>.ru</c> entry silently produces, reconstructed: the resolver answers
    /// with the neutral text, so the value IS present and IS English. A law asserting non-emptiness
    /// would accept this row.
    /// </remarks>
    [Fact]
    public void The_law_refuses_a_name_left_in_english_in_a_translated_file()
    {
        var untranslated = new List<ModuleDisplayName>
        {
            new("sites", "SitesModuleDisplayName", "Sites", "Sites", "Կայքեր"),
        };

        var violations = ModuleDisplayNameLaw.Violations(untranslated, NoExcuses());

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("the russian name is the english 'Sites'", StringComparison.Ordinal);
        });
    }

    /// <summary>The law refuses a module wearing another modules translation.</summary>
    /// <remarks>
    /// The realistic wrong translation, reconstructed: not invented prose but the neighbouring
    /// entry pasted into the wrong file. Every clause about presence and about differing from
    /// English is satisfied here — only the same-language collision sees it.
    /// </remarks>
    [Fact]
    public void The_law_refuses_a_module_wearing_another_modules_translation()
    {
        var pasted = new List<ModuleDisplayName>
        {
            new("sites", "SitesModuleDisplayName", "Sites", "Сайты", "Կայքեր"),
            new("firewall", "FirewallModuleDisplayName", "Firewall", "Сайты", "Պատնեշ"),
        };

        var violations = ModuleDisplayNameLaw.Violations(pasted, NoExcuses());

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("firewall and sites are both named 'Сайты' in russian", StringComparison.Ordinal);
        });
    }

    /// <summary>The law refuses a name that is the resource key itself.</summary>
    /// <remarks>
    /// What a reader sees when a key names no entry: <c>ResourceManager</c> answers with the key, so
    /// the catalogue prints <c>SitesModuleDisplayName</c> — the shape that shipped once already and
    /// that <see cref="ManifestUniquenessTests"/> now catches for absence but not for a key written
    /// INTO a value.
    /// </remarks>
    [Fact]
    public void The_law_refuses_a_name_that_is_the_resource_key_itself()
    {
        var raw = new List<ModuleDisplayName>
        {
            new("sites", "SitesModuleDisplayName", "SitesModuleDisplayName", "Сайты", "Կայքեր"),
        };

        var violations = ModuleDisplayNameLaw.Violations(raw, NoExcuses());

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("the english name IS the resource key", StringComparison.Ordinal);
        });
    }

    /// <summary>The law refuses an excuse the module no longer needs.</summary>
    /// <remarks>
    /// An exemption that has stopped describing its module is a hole under a reason that reads as
    /// considered — the failure mode <see cref="AccountCascadeTests"/> keeps its own staleness guard
    /// against.
    /// </remarks>
    [Fact]
    public void The_law_refuses_an_excuse_the_module_no_longer_needs()
    {
        var translated = new List<ModuleDisplayName>
        {
            new("ftp", "FtpModuleDisplayName", "FTPS", "Передача файлов по FTPS", "FTPS փոխանցում"),
        };

        var violations = ModuleDisplayNameLaw.Violations(translated, UntranslatedByDesign);

        Assert.Contains(violations, violation =>
        {
            return violation.Contains("its name is now translated", StringComparison.Ordinal);
        });
    }

    /// <summary>An empty exemption register, for the fixtures that need none.</summary>
    /// <returns>A register naming no module.</returns>
    private static Dictionary<string, string> NoExcuses()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Reads every shipped module's display name out of the authored resource files.</summary>
    /// <returns>One row per module manifest, ordered by module id.</returns>
    /// <remarks>
    /// <para>
    /// The MANIFESTS rather than <c>ModuleRegistry.All</c>, which is what
    /// <see cref="ManifestUniquenessTests"/> walks. A module ships its name before the host composes
    /// it — the Ftp module is in the tree, its manifest names <c>FtpModuleDisplayName</c>, and that
    /// name already reaches a customer on the upgrade screen, while the registry does not list it
    /// yet. A law that read the registry would report a clean tree for exactly the module whose name
    /// nothing else in this repository checks.
    /// </para>
    /// <para>
    /// The files rather than a resolved localizer, for the reason <see cref="ResourceTriples"/>
    /// states: a missing translated entry does not report itself, it serves the English text. The
    /// whole authored set is searched rather than one module's family, because these keys resolve
    /// against the pool every module registers into.
    /// </para>
    /// </remarks>
    private static List<ModuleDisplayName> AuthoredNames()
    {
        var english = ResourceTriples.ReadAll(culture: null);
        var russian = ResourceTriples.ReadAll("ru");
        var armenian = ResourceTriples.ReadAll("hy");

        return Manifests()
            .Select(manifest =>
            {
                var key = manifest.DisplayNameKey;
                return new ModuleDisplayName(
                    manifest.Id,
                    key,
                    OnlyValueOf(english, key),
                    OnlyValueOf(russian, key),
                    OnlyValueOf(armenian, key));
            })
            .OrderBy(name =>
            {
                return name.ModuleId;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every module manifest declared by the loaded module assemblies.</summary>
    /// <returns>The manifest each <c>*Manifest</c> type publishes as its <c>Instance</c>.</returns>
    private static List<Manifest> Manifests()
    {
        return DisplayNameLaw.LoadedProductAssemblies()
            .Where(assembly =>
            {
                return (assembly.GetName().Name ?? string.Empty)
                    .StartsWith("Maran.Modules.", StringComparison.Ordinal);
            })
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsPublic && type.Name.EndsWith("Manifest", StringComparison.Ordinal);
            })
            .Select(type =>
            {
                return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            })
            .Where(property =>
            {
                return property is not null && property.PropertyType == typeof(Manifest);
            })
            .Select(property =>
            {
                return (Manifest)property!.GetValue(null)!;
            })
            .ToList();
    }

    /// <summary>Reads the one value declared for a key across the authored files.</summary>
    /// <param name="values">The merged key-to-values map for one culture.</param>
    /// <param name="key">The resource key to read.</param>
    /// <returns>The value, or <c>null</c> when no file declares the key.</returns>
    /// <exception cref="InvalidOperationException">Two files declare the same key.</exception>
    /// <remarks>
    /// Two declarations of one key is not a case to pick a winner from: the modules share one
    /// resource pool, so whichever the pool answers with, one module is displaying the other's name
    /// — the collision <see cref="ManifestUniquenessTests"/> was written after shipping once.
    /// </remarks>
    private static string? OnlyValueOf(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string key)
    {
        if (!values.TryGetValue(key, out var declared))
        {
            return null;
        }

        if (declared.Count > 1)
        {
            throw new InvalidOperationException(
                $"'{key}' is declared in {declared.Count} authored resource files; the modules share "
                + "one pool, so one module is wearing another's name.");
        }

        return declared[0];
    }
}
