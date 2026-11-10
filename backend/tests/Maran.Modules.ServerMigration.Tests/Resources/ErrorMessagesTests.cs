using System.Globalization;
using System.Resources;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Resources;

/// <summary>What the administrator actually reads when the registry refuses something, in all three languages.</summary>
public sealed class ErrorMessagesTests
{
    /// <summary>The cultures this panel ships.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The module's error table, read the way the panel's resolver reads it.</summary>
    private static readonly ResourceManager Errors =
        new("Maran.Modules.ServerMigration.Resources.ErrorMessages", typeof(ServerMigrationModule).Assembly);

    /// <summary>The module's validator message table.</summary>
    private static readonly ResourceManager Validation =
        new("Maran.Modules.ServerMigration.Resources.ValidationMessages", typeof(ServerMigrationModule).Assembly);

    /// <summary>The module's display-name table.</summary>
    private static readonly ResourceManager Names =
        new("Maran.Modules.ServerMigration.Resources.DisplayNames", typeof(ServerMigrationModule).Assembly);

    /// <summary>Every code the module raises has a sentence in English, Russian and Armenian.</summary>
    [Fact]
    public void Every_code_this_module_raises_has_an_entry_in_all_three_locales()
    {
        Assert.NotEmpty(MigrationErrorCodes.All);
        Assert.NotEmpty(MigrationErrorCodes.Validation);

        foreach (var culture in Cultures)
        {
            foreach (var code in MigrationErrorCodes.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(Entry(Errors, code, culture)), $"{code} has no {culture} entry.");
            }

            foreach (var code in MigrationErrorCodes.Validation)
            {
                Assert.False(string.IsNullOrWhiteSpace(Entry(Validation, code, culture)), $"{code} has no {culture} entry.");
            }
        }
    }

    /// <summary>Control: a code nobody defined reads as absent, which is what lets the test above fail.</summary>
    [Fact]
    public void A_code_no_table_defines_reads_as_absent()
    {
        Assert.Null(Entry(Errors, "SomeCodeNobodyDefined", "ru"));
        Assert.Null(Entry(Validation, "SomeCodeNobodyDefined", "hy"));
    }

    /// <summary>The tables hold exactly the codes the tests list, so a new code is added to both on purpose.</summary>
    [Fact]
    public void The_tables_hold_exactly_the_listed_codes()
    {
        Assert.Equal(
            MigrationErrorCodes.All.Order(StringComparer.Ordinal).ToList(),
            Keys(Errors).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(
            MigrationErrorCodes.Validation.Order(StringComparer.Ordinal).ToList(),
            Keys(Validation).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>The Russian and Armenian sentences are translations, not the English text repeated.</summary>
    [Fact]
    public void The_russian_and_armenian_sentences_differ_from_the_english_one()
    {
        foreach (var code in MigrationErrorCodes.All)
        {
            var english = Entry(Errors, code, "en");
            Assert.NotEqual(english, Entry(Errors, code, "ru"));
            Assert.NotEqual(english, Entry(Errors, code, "hy"));
        }
    }

    /// <summary>No sentence carries a filesystem path or an en or em dash (rules/security.md item 8).</summary>
    [Fact]
    public void No_sentence_in_any_locale_names_a_path_or_uses_a_long_dash()
    {
        foreach (var culture in Cultures)
        {
            foreach (var code in MigrationErrorCodes.All)
            {
                var text = Entry(Errors, code, culture)!;
                Assert.DoesNotContain("/etc/", text, StringComparison.Ordinal);
                Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);
                Assert.DoesNotContain('–', text);
                Assert.DoesNotContain('—', text);
            }
        }
    }

    /// <summary>The names the manifest and the mappers ask for exist in each language.</summary>
    [Fact]
    public void The_display_names_the_module_asks_for_resolve_in_all_three_locales()
    {
        var keys = new List<string> { ServerMigrationManifest.Instance.DisplayNameKey };
        keys.AddRange(ServerMigrationManifest.Instance.Menu.Select(entry => { return entry.LabelKey; }));
        keys.AddRange(ServerMigrationManifest.Instance.Routes.Select(route => { return route.TitleKey; }));
        keys.AddRange(Enum.GetValues<RemoteServerState>().Select(value => { return "RemoteServerState" + value; }));
        keys.AddRange(Enum.GetValues<RemoteSourceKind>().Select(value => { return "RemoteSourceKind" + value; }));
        keys.AddRange(Enum.GetValues<RemoteFailureKind>().Select(value => { return "RemoteFailureKind" + value; }));
        keys.AddRange(Enum.GetValues<DiscoveredSiteBackend>().Select(value => { return "DiscoveredSiteBackend" + value; }));
        keys.AddRange(Enum.GetValues<DiscoveryWarningKind>().Select(value => { return "DiscoveryWarning" + value; }));

        foreach (var culture in Cultures)
        {
            foreach (var key in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(Entry(Names, key, culture)), $"{key} has no {culture} entry.");
            }
        }
    }

    /// <summary>One key read from the culture's own table, never from a parent culture it would fall back to.</summary>
    /// <param name="table">The resource table.</param>
    /// <param name="key">The entry's name.</param>
    /// <param name="culture">The culture whose own table is read.</param>
    /// <returns>The entry, or null when that culture's table does not define it.</returns>
    private static string? Entry(ResourceManager table, string key, string culture)
    {
        var set = table.GetResourceSet(new CultureInfo(culture), createIfNotExists: true, tryParents: false);

        return set?.GetString(key);
    }

    /// <summary>Every key the neutral table defines.</summary>
    /// <param name="table">The resource table.</param>
    /// <returns>The keys.</returns>
    private static List<string> Keys(ResourceManager table)
    {
        var set = table.GetResourceSet(new CultureInfo("en"), createIfNotExists: true, tryParents: false)!;
        var keys = new List<string>();
        var enumerator = set.GetEnumerator();
        while (enumerator.MoveNext())
        {
            keys.Add((string)enumerator.Key);
        }

        return keys;
    }
}
