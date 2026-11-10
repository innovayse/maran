using System.Globalization;
using System.Resources;
using System.Xml.Linq;

namespace Maran.Modules.Marketplace.Tests.Resources;

/// <summary>The module's resource triples hold the same keys, filled, in every language, and its error codes resolve.</summary>
public sealed class ResourceTripleTests
{
    /// <summary>Every error code the module defines, as the commands and the client raise them.</summary>
    private static readonly string[] ExpectedCodes =
    [
        "MarketplaceDisabled",
        "MarketplaceHostNotConfigured",
        "MarketplaceUnreachable",
        "CatalogueUnsigned",
        "CatalogueStale",
        "CatalogueKeyMissing",
        "LeaseRequired",
        "EntitlementMissing",
        "IncompatiblePanel",
        "PackageRevoked",
        "GrantHostRefused",
        "DownloadSizeMismatch",
        "DownloadDigestMismatch",
        "ModuleNotInCatalogue",
        "ModuleNotInstalled",
        "ModuleOperationInProgress",
        "ModuleVersionNotInstallable",
        "InstallNotAcknowledged",
    ];

    /// <summary>Every display name the module asks for.</summary>
    private static readonly string[] ExpectedDisplayNames =
    [
        "SentItemPanelVersion",
        "SentItemChannel",
        "SentItemLanguage",
        "SentItemLeaseCredential",
        "SentItemInstalledModules",
        "SentItemNothingElse",
        "EntitlementNotRequired",
        "EntitlementEntitled",
        "EntitlementNotPurchased",
        "EntitlementLeaseExpired",
        "EntitlementUnknown",
    ];

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
        var path = Path.Combine(directory.FullName, "src", "Maran.Modules", "Marketplace", "Resources", fileName);

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

    /// <summary>Each table has the same keys in English, Russian and Armenian, none empty.</summary>
    /// <param name="table">The table name.</param>
    [Theory]
    [InlineData("ErrorMessages")]
    [InlineData("DisplayNames")]
    public void The_triple_has_identical_filled_keys(string table)
    {
        var english = Read($"{table}.resx");
        var russian = Read($"{table}.ru.resx");
        var armenian = Read($"{table}.hy.resx");

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), russian.Keys.Order());
        Assert.Equal(english.Keys.Order(), armenian.Keys.Order());
        Assert.All(english.Concat(russian).Concat(armenian), pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key);
        });
    }

    /// <summary>The translations are translations: neither the Russian nor the Armenian text equals the English one.</summary>
    [Fact]
    public void The_translations_differ_from_the_english_text()
    {
        var english = Read("ErrorMessages.resx");

        Assert.All(Read("ErrorMessages.ru.resx"), pair => { Assert.NotEqual(english[pair.Key], pair.Value); });
        Assert.All(Read("ErrorMessages.hy.resx"), pair => { Assert.NotEqual(english[pair.Key], pair.Value); });
    }

    /// <summary>Error sentences carry no em dash or en dash and no path, address or version-shaped text in any language.</summary>
    [Fact]
    public void Error_sentences_carry_no_dash_and_no_address()
    {
        foreach (var file in new[] { "ErrorMessages.resx", "ErrorMessages.ru.resx", "ErrorMessages.hy.resx" })
        {
            Assert.All(Read(file), pair =>
            {
                Assert.DoesNotContain('–', pair.Value);
                Assert.DoesNotContain('—', pair.Value);
                Assert.DoesNotContain("http", pair.Value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain('/', pair.Value);
            });
        }
    }

    /// <summary>Every code the module raises is a key of the generated table, so it can never render as its own name.</summary>
    [Fact]
    public void The_generated_table_resolves_every_code()
    {
        var manager = new ResourceManager("Maran.Modules.Marketplace.Resources.ErrorMessages", typeof(MarketplaceModule).Assembly);

        Assert.All(ExpectedCodes, code =>
        {
            Assert.False(string.IsNullOrWhiteSpace(manager.GetString(code, CultureInfo.InvariantCulture)), code);
        });
        Assert.Null(manager.GetString("NoSuchCodeNobodyDefined", CultureInfo.InvariantCulture));
    }

    /// <summary>The names the manifest and the status screen ask for exist: the module name, the six sent lines and the five entitlement states.</summary>
    [Fact]
    public void The_display_names_the_module_asks_for_exist()
    {
        var names = Read("DisplayNames.resx");

        Assert.Contains(MarketplaceManifest.Instance.DisplayNameKey, names.Keys);
        Assert.All(
            ExpectedDisplayNames,
            key => { Assert.Contains(key, names.Keys); });
    }
}
