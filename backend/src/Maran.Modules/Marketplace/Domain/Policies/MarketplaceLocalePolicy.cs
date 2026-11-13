using System.Globalization;

namespace Maran.Modules.Marketplace.Domain.Policies;

/// <summary>
/// Chooses the language the panel asks the marketplace to localise its text in. The service answers
/// <c>400</c> to a <c>locale</c> query value outside <c>en</c>, <c>ru</c> and <c>hy</c>, so a culture
/// the contract does not list (the invariant culture of a background thread, a language the panel
/// gains later) is clamped to English here, in the one place every request takes its locale from,
/// instead of turning every browse into a refusal.
/// </summary>
public static class MarketplaceLocalePolicy
{
    /// <summary>The language used when the culture is not one the contract lists.</summary>
    public const string Fallback = "en";

    /// <summary>The languages the contract lists (<c>docs/marketplace/api-v1.md</c>, "Conventions").</summary>
    private static readonly string[] Supported = [Fallback, "ru", "hy"];

    /// <summary>The wire language for a culture.</summary>
    /// <param name="culture">The culture to translate; its two-letter language is what counts, so <c>ru-RU</c> is <c>ru</c>.</param>
    /// <returns>One of <c>en</c>, <c>ru</c>, <c>hy</c>.</returns>
    public static string For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var language = culture.TwoLetterISOLanguageName;
        return Array.Exists(Supported, supported => { return string.Equals(supported, language, StringComparison.Ordinal); })
            ? language
            : Fallback;
    }
}
