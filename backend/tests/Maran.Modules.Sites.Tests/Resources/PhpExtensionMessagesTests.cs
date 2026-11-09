using System.Xml.Linq;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Resources;

/// <summary>
/// The refusal sentences, support status words, availability words and extension labels of the PHP
/// extension and support life work (issue #157), in all three languages the panel ships.
/// </summary>
/// <remarks>
/// <para>
/// The keys are derived from the code that needs them, not listed twice: one label per name in
/// <see cref="PhpExtensionCatalogue"/> and one word per <see cref="PhpSupportStatus"/> and
/// <see cref="ExtensionAvailability"/> member, so a thirteenth extension or a fifth status added
/// without its translations fails here by name.
/// </para>
/// <para>
/// The resx files are read as XML rather than through a <c>ResourceManager</c>, which falls back to the
/// neutral text when a satellite assembly is missing and would then certify a translation that was
/// never built.
/// </para>
/// </remarks>
public sealed class PhpExtensionMessagesTests
{
    /// <summary>The locale suffixes of a resource file, neutral first.</summary>
    private static readonly string[] Locales = ["", ".ru", ".hy"];

    /// <summary>The refusal keys this change added to the error table.</summary>
    private static readonly string[] ErrorKeys =
        ["PhpExtensionNotAllowed", "PhpExtensionNotAvailable", "PhpExtensionToggleFailed"];

    /// <summary>The absolute-path roots a customer-facing sentence may never name.</summary>
    private static readonly string[] ForbiddenPathRoots =
        ["/etc/", "/var/", "/home/", "/root/", "/usr/", "/srv/", "/opt/", "/tmp/"];

    /// <summary>Every refusal key exists, non-empty, in the neutral, Russian and Armenian error tables.</summary>
    [Fact]
    public void Every_refusal_key_has_an_entry_in_all_three_error_tables()
    {
        Assert.NotEmpty(ErrorKeys);

        foreach (var locale in Locales)
        {
            var entries = Entries("ErrorMessages", locale);
            Assert.NotEmpty(entries);
            Assert.All(ErrorKeys, key =>
            {
                Assert.True(entries.ContainsKey(key), $"ErrorMessages{locale}.resx has no entry for {key}.");
                Assert.False(string.IsNullOrWhiteSpace(entries[key]), $"ErrorMessages{locale}.resx has an empty {key}.");
            });
        }
    }

    /// <summary>
    /// Every support status word and every catalogue extension label exists, non-empty, in all three
    /// display-name tables.
    /// </summary>
    [Fact]
    public void Every_status_word_and_extension_label_has_an_entry_in_all_three_display_tables()
    {
        var keys = DisplayKeys();
        Assert.Equal(
            Enum.GetValues<PhpSupportStatus>().Length
                + Enum.GetValues<ExtensionAvailability>().Length
                + PhpExtensionCatalogue.Names.Count,
            keys.Count);

        foreach (var locale in Locales)
        {
            var entries = Entries("DisplayNames", locale);
            Assert.All(keys, key =>
            {
                Assert.True(entries.ContainsKey(key), $"DisplayNames{locale}.resx has no entry for {key}.");
                Assert.False(string.IsNullOrWhiteSpace(entries[key]), $"DisplayNames{locale}.resx has an empty {key}.");
            });
        }
    }

    /// <summary>A translated sentence is a translation: not the key, not the English, not the other language.</summary>
    [Fact]
    public void Every_refusal_sentence_is_translated_in_russian_and_armenian()
    {
        var neutral = Entries("ErrorMessages", "");
        var russian = Entries("ErrorMessages", ".ru");
        var armenian = Entries("ErrorMessages", ".hy");

        Assert.All(ErrorKeys, key =>
        {
            Assert.NotEqual(key, russian[key]);
            Assert.NotEqual(key, armenian[key]);
            Assert.NotEqual(neutral[key], russian[key]);
            Assert.NotEqual(neutral[key], armenian[key]);
            Assert.NotEqual(russian[key], armenian[key]);
            Assert.Contains(russian[key], IsCyrillic);
            Assert.Contains(armenian[key], IsArmenian);
        });
    }

    /// <summary>The status words are translated into the language's own script.</summary>
    [Fact]
    public void Every_status_word_is_written_in_the_scripts_of_its_language()
    {
        var russian = Entries("DisplayNames", ".ru");
        var armenian = Entries("DisplayNames", ".hy");

        foreach (var key in Enum.GetNames<PhpSupportStatus>().Select(name => { return "PhpSupportStatus" + name; }))
        {
            Assert.Contains(russian[key], IsCyrillic);
            Assert.Contains(armenian[key], IsArmenian);
        }
    }

    /// <summary>Within one locale, no two extension labels and no two status words share a text by accident.</summary>
    [Fact]
    public void The_labels_differ_from_each_other_in_every_locale()
    {
        foreach (var locale in Locales)
        {
            var entries = Entries("DisplayNames", locale);
            var texts = DisplayKeys().Select(key =>
            {
                return entries[key];
            }).ToList();

            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>A refusal sentence names no path, no placeholder and no digit.</summary>
    /// <remarks>
    /// The refusal is deliberately silent about the host's packages and tool output (rules/security.md
    /// item 8), so a customer or an administrator reading it learns what happened and not how the host
    /// is arranged.
    /// </remarks>
    [Fact]
    public void A_refusal_sentence_names_no_path_placeholder_or_digit()
    {
        foreach (var locale in Locales)
        {
            var entries = Entries("ErrorMessages", locale);
            Assert.All(ErrorKeys, key =>
            {
                var value = entries[key];
                Assert.DoesNotContain(ForbiddenPathRoots, root => { return value.Contains(root, StringComparison.Ordinal); });
                Assert.DoesNotContain("{", value, StringComparison.Ordinal);
                Assert.DoesNotContain(value, character => { return char.IsAsciiDigit(character); });
            });
        }
    }

    /// <summary>
    /// Every display-name key this change added: status words, availability words, then one label per
    /// catalogue name.
    /// </summary>
    private static List<string> DisplayKeys()
    {
        return Enum.GetNames<PhpSupportStatus>()
            .Select(name =>
            {
                return "PhpSupportStatus" + name;
            })
            .Concat(Enum.GetNames<ExtensionAvailability>().Select(name =>
            {
                return "ExtensionAvailability" + name;
            }))
            .Concat(PhpExtensionCatalogue.Names.Select(name =>
            {
                return "PhpExtension" + char.ToUpperInvariant(name[0]) + name[1..];
            }))
            .ToList();
    }

    /// <summary>Says whether a character is in the Cyrillic block.</summary>
    /// <param name="character">The character to test.</param>
    private static bool IsCyrillic(char character)
    {
        return character is >= 'Ѐ' and <= 'ӿ';
    }

    /// <summary>Says whether a character is in the Armenian block.</summary>
    /// <param name="character">The character to test.</param>
    private static bool IsArmenian(char character)
    {
        return character is >= 'Ա' and <= '֏';
    }

    /// <summary>Reads one resx file into its key and value entries.</summary>
    /// <param name="table">The resource table, <c>ErrorMessages</c> or <c>DisplayNames</c>.</param>
    /// <param name="locale">The locale suffix: empty, <c>.ru</c> or <c>.hy</c>.</param>
    /// <returns>Every <c>&lt;data&gt;</c> entry by its <c>name</c>.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which is a failure and not an empty locale.</exception>
    private static Dictionary<string, string> Entries(string table, string locale)
    {
        var path = Path.Combine(BackendRoot(), "src", "Maran.Modules", "Sites", "Resources", $"{table}{locale}.resx");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A locale's resource file is missing, so it cannot be checked.", path);
        }

        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element =>
            {
                return element.Attribute("name")!.Value;
            },
            element =>
            {
                return element.Element("value")?.Value ?? string.Empty;
            },
            StringComparer.Ordinal);
    }

    /// <summary>Walks up from the test binary to the folder holding <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the backend root.</returns>
    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Maran.sln was not found above the test binary.");
    }
}
