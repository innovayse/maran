using System.Reflection;
using System.Xml.Linq;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Resources;

/// <summary>
/// The error codes the commands, queries and eligibility rules raise exist in all three languages, and the
/// spelled constants the tests assert against are exactly the keys of the neutral table.
/// </summary>
public sealed class OperationCodesResourceTests
{
    /// <summary>The codes the operation half added to the table.</summary>
    private static readonly string[] OperationCodes =
    [
        ErrorCodes.MarketplaceStorageUnavailable,
        ErrorCodes.ModuleAlreadyInstalled,
        ErrorCodes.ModuleHasNoPreviousVersion,
        ErrorCodes.ModuleOperationFailed,
        ErrorCodes.ModuleIdInvalid,
        ErrorCodes.ModuleVersionInvalid,
        ErrorCodes.MarketplaceChannelInvalid,
        ErrorCodes.MarketplaceSearchInvalid,
        ErrorCodes.MarketplaceCursorInvalid,
    ];

    /// <summary>Reads the keys of a resx file in the module's source tree.</summary>
    /// <param name="fileName">The file under the module's Resources folder.</param>
    /// <returns>The keys.</returns>
    private static HashSet<string> KeysOf(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "Maran.Modules", "Marketplace", "Resources", fileName);

        return XDocument.Load(path).Root!.Elements("data")
            .Select(element => { return (string)element.Attribute("name")!; })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Each code the operation half raises has an entry in the English, Russian and Armenian tables.</summary>
    [Fact]
    public void Every_operation_code_is_defined_in_all_three_languages()
    {
        foreach (var file in new[] { "ErrorMessages.resx", "ErrorMessages.ru.resx", "ErrorMessages.hy.resx" })
        {
            var keys = KeysOf(file);
            Assert.All(OperationCodes, code => { Assert.Contains(code, keys); });
        }
    }

    /// <summary>The tier names the catalogue shows exist in all three languages.</summary>
    [Fact]
    public void The_tier_names_exist_in_all_three_languages()
    {
        foreach (var file in new[] { "DisplayNames.resx", "DisplayNames.ru.resx", "DisplayNames.hy.resx" })
        {
            var keys = KeysOf(file);
            Assert.Contains("MarketplaceTierIncluded", keys);
            Assert.Contains("MarketplaceTierAddOn", keys);
            Assert.Contains("MarketplaceTierPlanGated", keys);
        }
    }

    /// <summary>The spelled constants are exactly the keys of the neutral error table, so a code cannot be renamed in one place only.</summary>
    [Fact]
    public void The_spelled_constants_are_exactly_the_neutral_table()
    {
        var spelled = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => { return (string)field.GetRawConstantValue()!; })
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(spelled.SetEquals(KeysOf("ErrorMessages.resx")));
    }
}
