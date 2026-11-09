using System.Xml.Linq;

namespace Maran.Modules.Accounts.Tests.Resources;

/// <summary>
/// Every reseller failure code and reseller display name exists in all three shipped languages, is
/// worded differently in each, and the three files of a table hold identical key sets
/// (rules/testing.md Definition of Done item 5).
/// </summary>
/// <remarks>
/// The files are read as XML rather than through the generated resource class, because a
/// <c>ResourceManager</c> falls back to the neutral text when a satellite is missing and would hide
/// an untranslated entry.
/// </remarks>
public sealed class ResellerMessagesTests
{
    /// <summary>The error codes the reseller role added; each is raised by a handler or the guard.</summary>
    private static readonly string[] ErrorKeys =
    [
        "ResellerNotFound",
        "ResellerNameTaken",
        "ResellerNameInvalid",
        "ResellerSuspended",
        "ResellerHasAccounts",
        "ResellerAccountLimitReached",
        "ResellerDiskPoolExceeded",
        "ResellerPlanLimitReached",
        "ResellerPlanExceedsEnvelope",
        "PlanOwnerMismatch",
        "ResellerLoginIssuanceFailed",
        "ResellerEnvelopeInvalid",
        "BrandingDisplayNameInvalid",
        "BrandingAccentColorInvalid",
        "BrandingAccentColorLowContrast",
        "BrandingLogoInvalid",
        "BrandingLogoTooLarge",
        "AccountOwnerUnchanged",
        "ResellerAllocationChanged",
        "BrandingChangedConcurrently",
    ];

    /// <summary>The display names the reseller role added: one per lifecycle state.</summary>
    private static readonly string[] DisplayKeys =
    [
        "ResellerStatusActive",
        "ResellerStatusSuspended",
    ];

    /// <summary>The language suffixes of one table's files, neutral first.</summary>
    private static readonly string[] Suffixes = ["", ".ru", ".hy"];

    /// <summary>Each error key crossed with each language file.</summary>
    /// <returns>The theory rows.</returns>
    public static TheoryData<string, string> ErrorKeysPerFile()
    {
        return RowsOf("ErrorMessages", ErrorKeys);
    }

    /// <summary>Each display key crossed with each language file.</summary>
    /// <returns>The theory rows.</returns>
    public static TheoryData<string, string> DisplayKeysPerFile()
    {
        return RowsOf("DisplayNames", DisplayKeys);
    }

    /// <summary>A reseller error code has a non-empty sentence in every language file.</summary>
    /// <param name="key">The error code.</param>
    /// <param name="file">The resx file name.</param>
    [Theory]
    [MemberData(nameof(ErrorKeysPerFile))]
    public void A_reseller_error_code_has_a_sentence_in_every_language_file(string key, string file)
    {
        var values = ValuesOf(file);

        Assert.True(values.ContainsKey(key), $"{file} has no entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(values[key]));
    }

    /// <summary>A reseller display name has a non-empty text in every language file.</summary>
    /// <param name="key">The display key.</param>
    /// <param name="file">The resx file name.</param>
    [Theory]
    [MemberData(nameof(DisplayKeysPerFile))]
    public void A_reseller_display_name_has_a_text_in_every_language_file(string key, string file)
    {
        var values = ValuesOf(file);

        Assert.True(values.ContainsKey(key), $"{file} has no entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(values[key]));
    }

    /// <summary>A reseller error code is worded differently in en, ru and hy, so none is a copied fallback.</summary>
    /// <param name="key">The error code.</param>
    [Theory]
    [InlineData("ResellerNotFound")]
    [InlineData("ResellerNameTaken")]
    [InlineData("ResellerNameInvalid")]
    [InlineData("ResellerSuspended")]
    [InlineData("ResellerHasAccounts")]
    [InlineData("ResellerAccountLimitReached")]
    [InlineData("ResellerDiskPoolExceeded")]
    [InlineData("ResellerPlanLimitReached")]
    [InlineData("ResellerPlanExceedsEnvelope")]
    [InlineData("PlanOwnerMismatch")]
    [InlineData("ResellerLoginIssuanceFailed")]
    [InlineData("ResellerEnvelopeInvalid")]
    [InlineData("AccountOwnerUnchanged")]
    [InlineData("ResellerAllocationChanged")]
    public void A_reseller_error_code_is_worded_differently_in_each_language(string key)
    {
        var sentences = Suffixes
            .Select(suffix =>
            {
                return ValuesOf($"ErrorMessages{suffix}.resx")[key];
            })
            .ToList();

        Assert.Equal(3, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The three files of each table hold exactly the same keys, so no language lacks one.</summary>
    /// <param name="table">The table's base name.</param>
    [Theory]
    [InlineData("ErrorMessages")]
    [InlineData("DisplayNames")]
    public void The_three_files_of_a_table_hold_the_same_keys(string table)
    {
        var neutral = ValuesOf($"{table}.resx").Keys.Order(StringComparer.Ordinal).ToList();

        foreach (var suffix in Suffixes.Skip(1))
        {
            var translated = ValuesOf($"{table}{suffix}.resx").Keys.Order(StringComparer.Ordinal).ToList();
            Assert.Equal(neutral, translated);
        }
    }

    /// <summary>
    /// The census sees keys at all: a table that read as empty would pass the key-set comparison above
    /// for the wrong reason, so the neutral table is asked to hold the reseller codes it was just
    /// checked for. Positive control of the census, on the axis that could silently go blind.
    /// </summary>
    [Fact]
    public void The_neutral_table_holds_every_reseller_error_code_the_census_expects()
    {
        var keys = ValuesOf("ErrorMessages.resx").Keys.ToHashSet(StringComparer.Ordinal);

        Assert.Subset(keys, ErrorKeys.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Crosses each key with each language file of a table.</summary>
    /// <param name="table">The table's base name.</param>
    /// <param name="keys">The keys to cross.</param>
    /// <returns>The theory rows.</returns>
    private static TheoryData<string, string> RowsOf(string table, IEnumerable<string> keys)
    {
        var data = new TheoryData<string, string>();
        foreach (var key in keys)
        {
            foreach (var suffix in Suffixes)
            {
                data.Add(key, $"{table}{suffix}.resx");
            }
        }

        return data;
    }

    /// <summary>Reads one resx file of the module into a key-to-text map.</summary>
    /// <param name="fileName">The resx file name inside the module's <c>Resources</c> folder.</param>
    /// <returns>Every <c>data</c> entry's name and value.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which fails the test.</exception>
    private static Dictionary<string, string> ValuesOf(string fileName)
    {
        var path = Path.Combine(BackendRoot(), "src", "Maran.Modules", "Accounts", "Resources", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A locale's resource file is missing.", path);
        }

        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(
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
