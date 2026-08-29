using System.Xml.Linq;
using Maran.Modules.Accounts.Errors;

namespace Maran.Modules.Accounts.Tests.Errors;

/// <summary>
/// Verifies every machine-stable code <see cref="AccountsErrors"/> can produce has a matching
/// <c>&lt;data&gt;</c> entry in all three of <c>Resources/ErrorMessages*.resx</c>
/// (rules/csharp.md "The backend owns all user-facing message text"), and that every resource
/// triple the module ships — <c>ErrorMessages</c> and <c>DisplayNames</c> — carries identical key
/// sets across English, Russian and Armenian. Reads the .resx files directly off disk rather than
/// through the compiled satellite assemblies, so a missing translation is caught even before
/// <see cref="AccountsModule"/>'s resource managers are wired into a running host.
/// </summary>
public sealed class AccountsErrorsTests
{
    /// <summary>Every error code <see cref="AccountsErrors"/> can currently produce.</summary>
    private static readonly IReadOnlyList<string> KnownErrorCodes =
    [
        AccountsErrors.NotFound(Guid.NewGuid()).Code,
        AccountsErrors.NameTaken("example").Code,
        AccountsErrors.DomainTaken("example.com").Code,
        AccountsErrors.PlanNotFound(Guid.NewGuid()).Code,
    ];

    /// <summary>Every resource base name this module ships, one per purpose (rules/csharp.md "One resource file per purpose").</summary>
    private static readonly IReadOnlyList<string> ResourceBaseNames = ["ErrorMessages", "DisplayNames"];

    /// <summary>Locale suffix (empty for invariant/English) used in each resx file name.</summary>
    private static readonly IReadOnlyList<string> LocaleSuffixes = ["", ".ru", ".hy"];

    [Theory]
    [MemberData(nameof(ErrorMessagesLocales))]
    public void Every_known_error_code_has_an_error_messages_resx_entry_in_this_locale(string localeSuffix)
    {
        var keys = ReadResxKeys("ErrorMessages", localeSuffix);

        foreach (var code in KnownErrorCodes)
        {
            Assert.True(keys.Contains(code), $"ErrorMessages{localeSuffix}.resx is missing the key '{code}'.");
        }
    }

    [Theory]
    [MemberData(nameof(ResourceTriples))]
    public void Every_resource_triple_declares_exactly_the_same_key_set_across_all_three_locales(string baseName)
    {
        var english = ReadResxKeys(baseName, "");
        var russian = ReadResxKeys(baseName, ".ru");
        var armenian = ReadResxKeys(baseName, ".hy");

        Assert.Equal(english, russian);
        Assert.Equal(english, armenian);
    }

    /// <summary>The locale suffixes exercised by <see cref="Every_known_error_code_has_an_error_messages_resx_entry_in_this_locale"/>.</summary>
    public static IEnumerable<object[]> ErrorMessagesLocales()
    {
        return LocaleSuffixes.Select(suffix =>
    {
        return new object[] { suffix };
    });
    }

    /// <summary>The resource base names exercised by <see cref="Every_resource_triple_declares_exactly_the_same_key_set_across_all_three_locales"/>.</summary>
    public static IEnumerable<object[]> ResourceTriples()
    {
        return ResourceBaseNames.Select(name =>
    {
        return new object[] { name };
    });
    }

    /// <summary>Reads every <c>&lt;data name="..."&gt;</c> key out of one locale's resx file.</summary>
    /// <param name="baseName">The resource base name, e.g. <c>ErrorMessages</c>.</param>
    /// <param name="localeSuffix">Empty for invariant/English, or <c>.ru</c>/<c>.hy</c>.</param>
    private static HashSet<string> ReadResxKeys(string baseName, string localeSuffix)
    {
        var path = Path.Combine(FindAccountsResourcesDirectory(), $"{baseName}{localeSuffix}.resx");
        var document = XDocument.Load(path);

        return document.Root!
            .Elements("data")
            .Select(element =>
            {
                return element.Attribute("name")!.Value;
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Locates <c>src/Maran.Modules/Accounts/Resources</c> by walking up from the test
    /// assembly's output directory to the repository's <c>backend</c> folder (identified by
    /// <c>Maran.sln</c>), since the test's own output directory varies by configuration.
    /// </summary>
    private static string FindAccountsResourcesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the backend directory (Maran.sln) above the test output.");
        }

        return Path.Combine(directory.FullName, "src", "Maran.Modules", "Accounts", "Resources");
    }
}
