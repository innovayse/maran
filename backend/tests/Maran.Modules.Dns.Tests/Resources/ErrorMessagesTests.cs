using System.Globalization;
using System.Resources;
using System.Xml.Linq;

namespace Maran.Modules.Dns.Tests.Resources;

/// <summary>The module's resource triples hold the same keys, filled, in every language.</summary>
public sealed class ErrorMessagesTests
{
    /// <summary>Reads the key and value pairs of a resx file in the module's source tree.</summary>
    /// <param name="fileName">The file under the module's Resources folder.</param>
    /// <returns>The entries by key.</returns>
    private static Dictionary<string, string> Read(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "Maran.Modules", "Dns", "Resources", fileName);

        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element =>
            {
                return (string)element.Attribute("name")!;
            },
            element =>
            {
                return (string)element.Element("value")!;
            });
    }

    /// <summary>Error messages have the same keys in English, Russian and Armenian, none empty.</summary>
    [Theory]
    [InlineData("ErrorMessages")]
    [InlineData("DisplayNames")]
    public void The_triple_has_identical_filled_keys(string table)
    {
        var english = Read($"{table}.resx");
        var russian = Read($"{table}.ru.resx");
        var armenian = Read($"{table}.hy.resx");

        Assert.Equal(english.Keys.Order(), russian.Keys.Order());
        Assert.Equal(english.Keys.Order(), armenian.Keys.Order());
        Assert.All(russian.Concat(armenian), pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key);
        });
    }

    /// <summary>Error sentences carry no em dash or en dash in any language.</summary>
    [Fact]
    public void Error_sentences_carry_no_dash()
    {
        foreach (var file in new[] { "ErrorMessages.resx", "ErrorMessages.ru.resx", "ErrorMessages.hy.resx" })
        {
            Assert.All(Read(file), pair =>
            {
                Assert.DoesNotContain('–', pair.Value);
                Assert.DoesNotContain('—', pair.Value);
            });
        }
    }

    /// <summary>Every code the module can raise is a key of the table, so it can never render as its own name.</summary>
    [Fact]
    public void The_generated_table_resolves_a_code()
    {
        var manager = new ResourceManager("Maran.Modules.Dns.Resources.ErrorMessages", typeof(DnsModule).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(manager.GetString("ZoneNotFound", CultureInfo.InvariantCulture)));
        Assert.False(string.IsNullOrWhiteSpace(manager.GetString("RecordDuplicate", CultureInfo.InvariantCulture)));
        Assert.Null(manager.GetString("NoSuchCodeNobodyDefined", CultureInfo.InvariantCulture));
    }
}
