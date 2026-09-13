using System.Globalization;
using Maran.Host.Modules;
using Maran.Host.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Covers the operator-facing name of a licence tier, and — the part that makes this a fix rather
/// than three translations — that EVERY tier the catalogue can carry has one, in all three
/// languages the panel ships.
/// </summary>
/// <remarks>
/// The pinned defect: the upgrade screen rendered <c>t('app.upgrade.tier', { tier: entry.tier })</c>
/// against the Russian sentence "Он доступен в тарифе {tier}.", so a Russian operator read
/// "Он доступен в тарифе addOn." — the contract constant verbatim inside localized text. Naming the
/// one tier that happened to be on screen would not have been a fix, because a member with no entry
/// falls back to the raw constant and IS the defect; so the closed set the rows come from,
/// <see cref="LicenceTier"/> itself, is walked here rather than sampled. Every module's manifest
/// declares a member of that enum and the catalogue reports it unchanged, so nothing can reach the
/// screen that this walk does not visit.
/// </remarks>
public sealed class LicenceTierDisplayNamesTests
{
    /// <summary>The languages the panel ships, whose resx files must each name every tier.</summary>
    /// <remarks>
    /// The invariant/neutral culture is covered by <c>"en"</c>: the neutral file IS the English one
    /// (rules/csharp.md "The backend owns all user-facing message text").
    /// </remarks>
    private static readonly string[] ShippedLanguages = ["en", "ru", "hy"];

    /// <summary>Every tier the catalogue can carry has a name an operator can read, in every language.</summary>
    /// <remarks>
    /// The whole enum, no exemptions, in all three languages — a member named in English and
    /// forgotten in <c>DisplayNames.ru.resx</c> resolves to the ENGLISH text, which is the same
    /// defect wearing a nicer coat, so Russian and Armenian are checked against the English answer
    /// as well as against the wire spelling. A member added to <see cref="LicenceTier"/> without an
    /// entry fails this test by name rather than reaching the upgrade screen as a constant.
    /// </remarks>
    [Fact]
    public void Every_licence_tier_has_a_name_an_operator_can_read_in_every_shipped_language()
    {
        var names = TierNames();
        var unnamed = new List<string>();

        foreach (var tier in Enum.GetValues<LicenceTier>())
        {
            var english = In("en", () => { return names.Of(tier); });

            foreach (var language in ShippedLanguages)
            {
                var name = In(language, () => { return names.Of(tier); });

                // The resolver's own fallback is the camelCase wire spelling, so a tier whose answer
                // is that spelling is a tier nobody named.
                if (string.Equals(name, WireSpellingOf(tier), StringComparison.Ordinal))
                {
                    unnamed.Add($"{language}/{tier}");
                    continue;
                }

                // A translated file that never claimed the key resolves to the neutral English text,
                // silently, and only the comparison against English can see it.
                if (!string.Equals(language, "en", StringComparison.Ordinal)
                    && string.Equals(name, english, StringComparison.Ordinal))
                {
                    unnamed.Add($"{language}/{tier}");
                }
            }
        }

        Assert.Empty(unnamed);
    }

    /// <summary>The probe that hunts unnamed tiers finds one when there is one to find.</summary>
    /// <remarks>
    /// The positive control the census owes (rules/testing.md): the walk above reports on absence,
    /// and absence is exactly what a broken probe also reports. A value outside the enum is a tier
    /// no resx can possibly claim, so the resolver must answer with the fallback the census treats
    /// as "unnamed" — proving the census can still see one.
    /// </remarks>
    [Fact]
    public void A_tier_no_resource_file_claims_is_answered_with_the_fallback_the_census_hunts()
    {
        var names = TierNames();

        Assert.Equal("999", In("ru", () => { return names.Of((LicenceTier)999); }));
    }

    /// <summary>The English name of the add-on tier is the phrase it should be, stated exactly.</summary>
    /// <remarks>
    /// A test asserting "a name came back" would pass against the screen full of raw constants,
    /// which is the defect. The VALUE is asserted (rules/testing.md).
    /// </remarks>
    [Fact]
    public void The_english_name_of_the_add_on_tier_is_the_phrase_it_should_be()
    {
        var names = TierNames();

        Assert.Equal("Add-on", In("en", () => { return names.Of(LicenceTier.AddOn); }));
    }

    /// <summary>In Russian the tier is Russian, which is the whole of what was measured as broken.</summary>
    /// <remarks>
    /// The exact screen from the defect: "Он доступен в тарифе addOn." The assertion is on the
    /// Russian VALUE, so a build that resolved the neutral English text — the state a missing
    /// <c>.ru</c> entry silently produces — fails here.
    /// </remarks>
    [Fact]
    public void The_russian_names_of_the_licence_tiers_are_russian()
    {
        var names = TierNames();

        Assert.Equal("Дополнительный модуль", In("ru", () => { return names.Of(LicenceTier.AddOn); }));
        Assert.Equal("Входит в поставку", In("ru", () => { return names.Of(LicenceTier.Included); }));
        Assert.Equal("Входит в старший тарифный план", In("ru", () => { return names.Of(LicenceTier.PlanGated); }));
    }

    /// <summary>Builds the tier-name resolver over the Host's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    /// <remarks>
    /// A stub localizer would prove only that the endpoint calls something; the thing worth checking
    /// is that every tier a catalogue row can carry HAS an entry under this key scheme, and that is
    /// only observable against the real resource files (rules/testing.md "A check must be able to
    /// observe what it reports on"). Same arrangement as the module resolvers' tests.
    /// </remarks>
    private static LicenceTierDisplayNames TierNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new LicenceTierDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }

    /// <summary>Runs one lookup with the panel's request culture set to a given language.</summary>
    /// <param name="language">The UI culture to read the resources in.</param>
    /// <param name="lookup">The lookup to perform.</param>
    /// <returns>What the resolver answered in that language.</returns>
    /// <remarks>
    /// The culture is restored in a <c>finally</c>: xunit runs test classes in parallel, and a
    /// leaked <see cref="CultureInfo.CurrentUICulture"/> would make another test read a language
    /// nobody set.
    /// </remarks>
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

    /// <summary>The wire spelling of a member — the camelCase form the JSON surface carries.</summary>
    /// <param name="tier">The member to spell.</param>
    /// <returns>The member's name with its first letter lowered, e.g. <c>addOn</c>.</returns>
    private static string WireSpellingOf(LicenceTier tier)
    {
        var member = tier.ToString();

        return char.ToLowerInvariant(member[0]) + member[1..];
    }
}
