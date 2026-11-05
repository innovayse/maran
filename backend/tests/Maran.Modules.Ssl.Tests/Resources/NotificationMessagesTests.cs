using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Maran.Modules.Ssl.Tests.Resources;

/// <summary>
/// The operator alert texts of this module in all three languages the panel ships (issue #161): the
/// same keys, each translated, each taking the same placeholders.
/// </summary>
/// <remarks>
/// The resx files are read as XML rather than through a <c>ResourceManager</c>, which falls back to
/// the neutral text when a satellite assembly is missing and would then certify a translation that
/// was never built.
/// </remarks>
public sealed class NotificationMessagesTests
{
    /// <summary>The pattern of a numbered composite-format placeholder: <c>{0}</c>, <c>{1}</c> and so on.</summary>
    private const string PlaceholderPattern = @"\{\d+\}";

    /// <summary>The resx file of each locale, neutral first.</summary>
    private static readonly string[] FileNames =
    [
        "NotificationMessages.resx",
        "NotificationMessages.ru.resx",
        "NotificationMessages.hy.resx",
    ];

    /// <summary>The keys the expiry alert is composed of.</summary>
    private static readonly string[] ExpectedKeys =
    [
        "CertificateExpiryAlertSubject",
        "CertificateExpiryAlertBody",
        "CertificateExpiryAlertLastFailure",
    ];

    /// <summary>The neutral, Russian and Armenian files define exactly the same keys, and the expected ones.</summary>
    [Fact]
    public void The_three_locales_define_the_same_non_empty_keys()
    {
        var neutral = Entries(FileNames[0]);
        Assert.NotEmpty(neutral);
        Assert.All(ExpectedKeys, key => { Assert.True(neutral.ContainsKey(key), $"Neutral file has no {key}."); });

        foreach (var fileName in FileNames)
        {
            var entries = Entries(fileName);
            Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), entries.Keys.Order(StringComparer.Ordinal));
            Assert.All(entries, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{fileName} has an empty {entry.Key}.");
            });
        }
    }

    /// <summary>A translated text is a translation: not the key, not the English, not the other language.</summary>
    [Fact]
    public void Every_text_is_translated_in_russian_and_armenian()
    {
        var neutral = Entries(FileNames[0]);
        var russian = Entries(FileNames[1]);
        var armenian = Entries(FileNames[2]);

        Assert.All(ExpectedKeys, key =>
        {
            Assert.NotEqual(key, russian[key]);
            Assert.NotEqual(key, armenian[key]);
            Assert.NotEqual(neutral[key], russian[key]);
            Assert.NotEqual(neutral[key], armenian[key]);
            Assert.NotEqual(russian[key], armenian[key]);
        });
    }

    /// <summary>Every locale's text takes the same placeholders as the neutral one, so none drops a figure.</summary>
    [Fact]
    public void Every_locale_uses_the_same_placeholders_as_the_neutral_text()
    {
        var neutral = Entries(FileNames[0]);

        foreach (var fileName in FileNames.Skip(1))
        {
            var entries = Entries(fileName);
            Assert.All(ExpectedKeys, key =>
            {
                Assert.Equal(Placeholders(neutral[key]), Placeholders(entries[key]));
            });
        }
    }

    /// <summary>The alert texts name no filesystem path in any locale.</summary>
    [Fact]
    public void No_text_names_a_path()
    {
        foreach (var fileName in FileNames)
        {
            Assert.All(Entries(fileName), entry =>
            {
                Assert.DoesNotContain("/", entry.Value, StringComparison.Ordinal);
            });
        }
    }

    /// <summary>The distinct placeholder indexes a text uses, in order.</summary>
    /// <param name="text">A resource text.</param>
    /// <returns>The indexes of its <c>{n}</c> placeholders.</returns>
    private static List<string> Placeholders(string text)
    {
        return [.. Regex.Matches(text, PlaceholderPattern).Select(match =>
        {
            return match.Value;
        }).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>Reads one resx file into its key and value entries.</summary>
    /// <param name="fileName">The resx file name inside the module's Resources folder.</param>
    /// <returns>Every <c>&lt;data&gt;</c> entry by its <c>name</c>.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which is a failure and not an empty locale.</exception>
    private static Dictionary<string, string> Entries(string fileName)
    {
        var path = Path.Combine(BackendRoot(), "src", "Maran.Modules", "Ssl", "Resources", fileName);
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
