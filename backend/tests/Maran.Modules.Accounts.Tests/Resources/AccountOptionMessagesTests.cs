using System.Xml.Linq;

namespace Maran.Modules.Accounts.Tests.Resources;

/// <summary>
/// The refusals the new-account options can raise (issue #194) exist in all three shipped languages,
/// are worded differently in each, and name the bounds the agent enforces
/// (rules/testing.md Definition of Done item 5).
/// </summary>
/// <remarks>
/// The files are read as XML rather than through the generated resource class, because a
/// <c>ResourceManager</c> falls back to the neutral text when a satellite is missing and would hide an
/// untranslated entry.
/// </remarks>
public sealed class AccountOptionMessagesTests
{
    /// <summary>The three resx files of the module, neutral first.</summary>
    private static readonly string[] FileNames = ["ErrorMessages.resx", "ErrorMessages.ru.resx", "ErrorMessages.hy.resx"];

    /// <summary>The codes the options raise: four from the validator and one from the creation handler.</summary>
    private static readonly string[] Keys =
    [
        "AccountInitialPasswordInvalid",
        "AccountProcessLimitInvalid",
        "AccountInodeLimitInvalid",
        "AccountOpenFilesLimitInvalid",
        "AccountLimitAbovePlan",
    ];

    /// <summary>Each key crossed with each resx file name.</summary>
    /// <returns>The theory rows.</returns>
    public static TheoryData<string, string> KeysPerFile()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in Keys)
        {
            foreach (var file in FileNames)
            {
                data.Add(key, file);
            }
        }

        return data;
    }

    /// <summary>Each option refusal has a non-empty sentence in every language file.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="file">The resx file name.</param>
    [Theory]
    [MemberData(nameof(KeysPerFile))]
    public void An_option_refusal_has_a_sentence_in_every_language_file(string key, string file)
    {
        var values = ValuesOf(file);

        Assert.True(values.ContainsKey(key), $"{file} has no entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(values[key]));
    }

    /// <summary>Each option refusal is worded differently in en, ru and hy, so none is a copied fallback.</summary>
    /// <param name="key">The resource key.</param>
    [Theory]
    [InlineData("AccountInitialPasswordInvalid")]
    [InlineData("AccountProcessLimitInvalid")]
    [InlineData("AccountInodeLimitInvalid")]
    [InlineData("AccountOpenFilesLimitInvalid")]
    [InlineData("AccountLimitAbovePlan")]
    public void An_option_refusal_is_worded_differently_in_each_language(string key)
    {
        var sentences = FileNames.Select(file => { return ValuesOf(file)[key]; }).ToList();

        Assert.Equal(3, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The range sentences state the agent's bounds in every language.</summary>
    /// <param name="file">The resx file name.</param>
    [Theory]
    [InlineData("ErrorMessages.resx")]
    [InlineData("ErrorMessages.ru.resx")]
    [InlineData("ErrorMessages.hy.resx")]
    public void The_range_sentences_state_the_agents_bounds(string file)
    {
        var values = ValuesOf(file);

        Assert.Contains("4194304", values["AccountProcessLimitInvalid"], StringComparison.Ordinal);
        Assert.Contains("4294967295", values["AccountInodeLimitInvalid"], StringComparison.Ordinal);
        Assert.Contains("1048576", values["AccountOpenFilesLimitInvalid"], StringComparison.Ordinal);
    }

    /// <summary>Reads one resx file of the module into a key-to-text map.</summary>
    /// <param name="fileName">The resx file name inside the module's <c>Resources</c> folder.</param>
    /// <returns>Every <c>data</c> entry's name and value.</returns>
    /// <exception cref="FileNotFoundException">The file is missing, which fails the test rather than reading as an empty language.</exception>
    private static Dictionary<string, string> ValuesOf(string fileName)
    {
        var path = Path.Combine(BackendRoot(), "src", "Maran.Modules", "Accounts", "Resources", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A locale's resource file is missing.", path);
        }

        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(
                element => { return element.Attribute("name")!.Value; },
                element => { return element.Element("value")?.Value ?? string.Empty; },
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

        throw new InvalidOperationException("Maran.sln was not found above the test binary.");
    }
}
