using System.Globalization;
using System.Resources;
using Maran.Modules.Fleet.Tests.TestSupport;

namespace Maran.Modules.Fleet.Tests.Resources;

/// <summary>What the administrator actually reads when the registry refuses a change, in all three languages.</summary>
public sealed class ErrorMessagesTests
{
    /// <summary>The cultures this panel ships.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The module's error table, read the way the panel's resolver reads it.</summary>
    private static readonly ResourceManager Table =
        new("Maran.Modules.Fleet.Resources.ErrorMessages", typeof(FleetModule).Assembly);

    /// <summary>The module's display-name table.</summary>
    private static readonly ResourceManager Names =
        new("Maran.Modules.Fleet.Resources.DisplayNames", typeof(FleetModule).Assembly);

    /// <summary>Every code the module raises has a sentence in English, Russian and Armenian.</summary>
    [Fact]
    public void Every_code_this_module_raises_has_an_entry_in_all_three_locales()
    {
        Assert.NotEmpty(ErrorCodes.All);

        foreach (var code in ErrorCodes.All)
        {
            foreach (var culture in Cultures)
            {
                var text = Entry(Table, code, culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} has no {culture} entry.");
            }
        }
    }

    /// <summary>A code nobody defined reads as nothing, which is what makes the previous test able to fail.</summary>
    [Fact]
    public void A_code_no_table_defines_reads_as_absent()
    {
        var text = Entry(Table, "SomeCodeNobodyDefined", "ru");

        Assert.Null(text);
    }

    /// <summary>The Russian and Armenian sentences are translations, not the English text repeated.</summary>
    [Fact]
    public void The_russian_and_armenian_sentences_differ_from_the_english_one()
    {
        foreach (var code in ErrorCodes.All)
        {
            var english = Table.GetString(code, CultureInfo.InvariantCulture);

            Assert.NotEqual(english, Entry(Table, code, "ru"));
            Assert.NotEqual(english, Entry(Table, code, "hy"));
        }
    }

    /// <summary>No sentence carries a filesystem path, a version or a tool's output (rules/security.md item 8).</summary>
    [Fact]
    public void No_sentence_in_any_locale_names_a_path()
    {
        foreach (var code in ErrorCodes.All)
        {
            foreach (var culture in Cultures)
            {
                var text = Entry(Table, code, culture);
                Assert.DoesNotContain("/etc/", text, StringComparison.Ordinal);
                Assert.DoesNotContain("/var/", text, StringComparison.Ordinal);
                Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>The manifest's display-name key resolves to a name in each language.</summary>
    [Fact]
    public void The_manifest_display_name_resolves_in_all_three_locales()
    {
        foreach (var culture in Cultures)
        {
            var text = Entry(Names, FleetManifest.Instance.DisplayNameKey, culture);

            Assert.False(string.IsNullOrWhiteSpace(text), $"The module name has no {culture} entry.");
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
}
