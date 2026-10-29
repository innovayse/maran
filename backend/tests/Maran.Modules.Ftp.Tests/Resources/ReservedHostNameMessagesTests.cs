using System.Xml.Linq;

namespace Maran.Modules.Ftp.Tests.Resources;

/// <summary>
/// The reserved-name refusal sentences of this module, in all three languages the panel ships
/// (issue #96).
/// </summary>
/// <remarks>
/// <para>
/// The validator tests assert a refusal's KEY. These assert the SENTENCE behind each key, because a
/// key with no translation reaches a Russian or Armenian customer as an English sentence or as the
/// bare identifier, and neither is a build or a runtime error. Key parity across the three files is
/// also swept repository-wide by the architecture tests; this file adds the properties that sweep
/// cannot see: the value is non-empty, is a real translation, and carries no path and no host name
/// (rules/security.md item 8: a reserved-name refusal must not say which rule matched or echo the
/// name it refused).
/// </para>
/// <para>
/// The resx files are read as XML rather than through a <c>ResourceManager</c>, which falls back to
/// the neutral text when a satellite assembly is missing and would then certify a translation that
/// was never built.
/// </para>
/// </remarks>
public sealed class ReservedHostNameMessagesTests
{
    /// <summary>The resx file of each locale, neutral first.</summary>
    private static readonly string[] FileNames =
    [
        "ErrorMessages.resx",
        "ErrorMessages.ru.resx",
        "ErrorMessages.hy.resx",
    ];

    /// <summary>The refusal keys this change added to the module.</summary>
    private static readonly string[] ReservedKeys = ["FtpsHostnameReserved"];

    /// <summary>The absolute-path roots a customer-facing sentence may never name.</summary>
    private static readonly string[] ForbiddenPathRoots =
        ["/etc/", "/var/", "/home/", "/root/", "/usr/", "/srv/", "/opt/", "/tmp/"];

    /// <summary>Every reserved-name key exists in the neutral, Russian and Armenian files.</summary>
    /// <remarks>
    /// The per-file <c>NotEmpty</c> is the vacuity guard: a key list or a file that came back empty
    /// would otherwise pass the loop below over nothing.
    /// </remarks>
    [Fact]
    public void Every_reserved_name_key_has_an_entry_in_all_three_locales()
    {
        Assert.NotEmpty(ReservedKeys);

        foreach (var fileName in FileNames)
        {
            var entries = Entries(fileName);
            Assert.NotEmpty(entries);
            Assert.All(ReservedKeys, key =>
            {
                Assert.True(entries.ContainsKey(key), $"{fileName} has no entry for {key}.");
                Assert.False(string.IsNullOrWhiteSpace(entries[key]), $"{fileName} has an empty {key}.");
            });
        }
    }

    /// <summary>A translated reserved-name sentence is a translation, not the key or the English.</summary>
    /// <remarks>
    /// The two realistic defects: a key copied into a translation file untranslated, and a sentence
    /// pasted from a neighbouring key. Each translated value must differ from its key, from the
    /// English text of the same key, and from the other translation's text of that key.
    /// </remarks>
    [Fact]
    public void Every_reserved_name_sentence_is_translated_in_russian_and_armenian()
    {
        var neutral = Entries(FileNames[0]);
        var russian = Entries(FileNames[1]);
        var armenian = Entries(FileNames[2]);

        Assert.All(ReservedKeys, key =>
        {
            Assert.NotEqual(key, russian[key]);
            Assert.NotEqual(key, armenian[key]);
            Assert.NotEqual(neutral[key], russian[key]);
            Assert.NotEqual(neutral[key], armenian[key]);
            Assert.NotEqual(russian[key], armenian[key]);
        });
    }

    /// <summary>Within one locale, no two reserved-name keys share a sentence by accident.</summary>
    /// <remarks>
    /// A paste of one key's text into its sibling reads as fluent text and answers the wrong
    /// question; the module's message-collision architecture test is the repository-wide net, and
    /// this pins it for the keys of this change.
    /// </remarks>
    [Fact]
    public void The_reserved_name_sentences_differ_from_each_other_in_every_locale()
    {
        foreach (var fileName in FileNames)
        {
            var entries = Entries(fileName);
            var texts = ReservedKeys.Select(key =>
            {
                return entries[key];
            }).ToList();

            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>A reserved-name sentence names no path, no address and no placeholder.</summary>
    /// <remarks>
    /// The refusal is deliberately silent about WHICH rule matched and about the name itself, so a
    /// customer cannot use it to learn the panel's host name or the shape of the reserved set. A
    /// digit-bearing or brace-bearing sentence would be the first sign of one being echoed.
    /// </remarks>
    [Fact]
    public void A_reserved_name_sentence_names_no_path_address_or_placeholder()
    {
        foreach (var fileName in FileNames)
        {
            var entries = Entries(fileName);
            Assert.All(ReservedKeys, key =>
            {
                var value = entries[key];
                Assert.DoesNotContain(ForbiddenPathRoots, root =>
                {
                    return value.Contains(root, StringComparison.Ordinal);
                });
                Assert.DoesNotContain("{", value, StringComparison.Ordinal);
                Assert.DoesNotContain("localhost", value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(value, character =>
                {
                    return char.IsAsciiDigit(character);
                });
            });
        }
    }

    /// <summary>Reads one resx file into its key and value entries.</summary>
    /// <param name="fileName">The resx file name inside the module's Resources folder.</param>
    /// <returns>Every <c>&lt;data&gt;</c> entry by its <c>name</c>.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which is a failure and not an empty locale.</exception>
    private static Dictionary<string, string> Entries(string fileName)
    {
        var path = Path.Combine(BackendRoot(), "src", "Maran.Modules", "Ftp", "Resources", fileName);
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

        throw new InvalidOperationException("Maran.sln not found above the test output directory.");
    }
}
