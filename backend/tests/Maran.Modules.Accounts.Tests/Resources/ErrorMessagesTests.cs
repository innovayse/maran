using System.Xml.Linq;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Resources;

/// <summary>
/// The account-name refusals exist in all three shipped languages, say different things, and are
/// the very codes the create validator emits (rules/testing.md Definition of Done item 5).
/// </summary>
/// <remarks>
/// The files are read as XML rather than through the generated resource class, because a
/// <c>ResourceManager</c> falls back to the neutral text when a satellite is missing and would hide
/// an untranslated entry.
/// </remarks>
public sealed class ErrorMessagesTests
{
    /// <summary>The three resx files of the module, neutral first.</summary>
    private static readonly string[] FileNames =
    [
        "ErrorMessages.resx",
        "ErrorMessages.ru.resx",
        "ErrorMessages.hy.resx",
    ];

    /// <summary>The two keys the narrowed account-name rules answer with.</summary>
    public static TheoryData<string> NameKeys()
    {
        return new TheoryData<string> { "AccountNameInvalid", "AccountNameReserved" };
    }

    /// <summary>Each key crossed with each resx file name.</summary>
    public static TheoryData<string, string> NameKeysPerFile()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in new[] { "AccountNameInvalid", "AccountNameReserved" })
        {
            foreach (var file in FileNames)
            {
                data.Add(key, file);
            }
        }

        return data;
    }

    /// <summary>A name refusal has a non-empty sentence in every language file.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="file">The resx file name.</param>
    [Theory]
    [MemberData(nameof(NameKeysPerFile))]
    public void A_name_refusal_has_a_sentence_in_every_language_file(string key, string file)
    {
        var values = ValuesOf(file);

        Assert.True(values.ContainsKey(key), $"{file} has no entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(values[key]));
    }

    /// <summary>A name refusal is worded differently in en, ru and hy, so none is a copied fallback.</summary>
    /// <param name="key">The resource key.</param>
    [Theory]
    [MemberData(nameof(NameKeys))]
    public void A_name_refusal_is_worded_differently_in_each_language(string key)
    {
        var sentences = FileNames
            .Select(file =>
            {
                return ValuesOf(file)[key];
            })
            .ToList();

        Assert.Equal(3, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The neutral text states the 30-character bound and does not offer the hyphen the agent refuses.</summary>
    [Fact]
    public void The_malformed_name_sentence_states_the_agents_bound_and_offers_no_hyphen()
    {
        var english = ValuesOf("ErrorMessages.resx")["AccountNameInvalid"];

        Assert.Contains("30", english, StringComparison.Ordinal);
        Assert.DoesNotContain("hyphen", english, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-", english, StringComparison.Ordinal);
    }

    /// <summary>The codes the validator emits for a bad name are keys that every language file answers.</summary>
    /// <param name="name">A name the validator refuses.</param>
    /// <param name="code">The code it is expected to refuse with.</param>
    [Theory]
    [InlineData("root", "AccountNameReserved")]
    [InlineData("maran_api", "AccountNameReserved")]
    [InlineData("acme-01", "AccountNameInvalid")]
    [InlineData("ab", "AccountNameInvalid")]
    public async Task The_code_the_validator_emits_for_a_bad_name_is_a_key_in_every_language_file(string name, string code)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AccountsDbContext(options);
        var planId = Guid.NewGuid();
        dbContext.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        await dbContext.SaveChangesAsync();

        var result = await new CreateAccountCommandValidator(dbContext).ValidateAsync(
            new CreateAccountCommand(name, "acme.example.com", planId, "owner@example.com"));

        var emitted = Assert.Single(result.Errors).ErrorMessage;
        Assert.Equal(code, emitted);
        foreach (var file in FileNames)
        {
            Assert.True(ValuesOf(file).ContainsKey(emitted), $"{file} has no entry named {emitted}.");
        }
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
