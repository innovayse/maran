using System.Globalization;
using Maran.Host.Modules;
using Maran.Host.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Covers the operator-facing name of a module kind, and that EVERY kind the catalogue can carry
/// has one in all three languages the panel ships.
/// </summary>
/// <remarks>
/// Built on the same arrangement as <see cref="LicenceTierDisplayNamesTests"/>: the whole enum is
/// walked rather than sampled, against the Host's real resource files, because a member with no
/// entry falls back to the wire constant and a translated file that never claimed the key falls
/// back to English, and only walking every language can see either.
/// </remarks>
public sealed class ModuleKindDisplayNamesTests
{
    /// <summary>The languages the panel ships, whose resx files must each name every kind.</summary>
    private static readonly string[] ShippedLanguages = ["en", "ru", "hy"];

    /// <summary>Every module kind has a name an operator can read, in every shipped language.</summary>
    [Fact]
    public void Every_module_kind_has_a_name_an_operator_can_read_in_every_shipped_language()
    {
        var names = KindNames();
        var unnamed = new List<string>();

        foreach (var kind in Enum.GetValues<ModuleKind>())
        {
            var english = In("en", () => { return names.Of(kind); });

            foreach (var language in ShippedLanguages)
            {
                var name = In(language, () => { return names.Of(kind); });

                if (string.Equals(name, WireSpellingOf(kind), StringComparison.Ordinal))
                {
                    unnamed.Add($"{language}/{kind}");
                    continue;
                }

                if (!string.Equals(language, "en", StringComparison.Ordinal)
                    && string.Equals(name, english, StringComparison.Ordinal))
                {
                    unnamed.Add($"{language}/{kind}");
                }
            }
        }

        Assert.Empty(unnamed);
    }

    /// <summary>The probe that hunts unnamed kinds finds one when there is one to find.</summary>
    /// <remarks>The positive control the census owes (rules/testing.md): a value outside the enum is a kind no resx can claim.</remarks>
    [Fact]
    public void A_kind_no_resource_file_claims_is_answered_with_the_fallback_the_census_hunts()
    {
        var names = KindNames();

        Assert.Equal("999", In("ru", () => { return names.Of((ModuleKind)999); }));
    }

    /// <summary>The English name of a packageable module is the phrase it should be, stated exactly.</summary>
    [Fact]
    public void The_english_name_of_a_packageable_module_is_the_phrase_it_should_be()
    {
        var names = KindNames();

        Assert.Equal("Installed separately", In("en", () => { return names.Of(ModuleKind.Packageable); }));
    }

    /// <summary>Builds the kind-name resolver over the Host's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    private static ModuleKindDisplayNames KindNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new ModuleKindDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }

    /// <summary>Runs one lookup with the panel's request culture set to a given language.</summary>
    /// <param name="language">The UI culture to read the resources in.</param>
    /// <param name="lookup">The lookup to perform.</param>
    /// <returns>What the resolver answered in that language.</returns>
    private static string In(string language, Func<string> lookup)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);

            return lookup();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The wire spelling of a member, the camelCase form the JSON surface carries.</summary>
    /// <param name="kind">The member to spell.</param>
    /// <returns>The member's name with its first letter lowered.</returns>
    private static string WireSpellingOf(ModuleKind kind)
    {
        var member = kind.ToString();

        return char.ToLowerInvariant(member[0]) + member[1..];
    }
}
