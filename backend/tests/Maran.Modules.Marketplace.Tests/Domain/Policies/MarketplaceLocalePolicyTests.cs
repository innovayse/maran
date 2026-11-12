using System.Globalization;
using Maran.Modules.Marketplace.Domain.Policies;

namespace Maran.Modules.Marketplace.Tests.Domain.Policies;

/// <summary>The locale the panel sends is always one the marketplace contract lists, whatever culture the thread runs in.</summary>
public sealed class MarketplaceLocalePolicyTests
{
    /// <summary>A culture of a listed language, with or without a region, is sent as that language.</summary>
    /// <param name="culture">The culture name.</param>
    /// <param name="expected">The wire language.</param>
    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("hy", "hy")]
    [InlineData("hy-AM", "hy")]
    public void A_listed_language_is_sent_as_it_is(string culture, string expected)
    {
        Assert.Equal(expected, MarketplaceLocalePolicy.For(CultureInfo.GetCultureInfo(culture)));
    }

    /// <summary>The invariant culture and a language the contract does not list fall back to English instead of reaching the service as a value it refuses.</summary>
    /// <param name="culture">The culture name; empty for the invariant culture.</param>
    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    [InlineData("fr")]
    [InlineData("ja-JP")]
    public void Anything_else_falls_back_to_english(string culture)
    {
        Assert.Equal(MarketplaceLocalePolicy.Fallback, MarketplaceLocalePolicy.For(CultureInfo.GetCultureInfo(culture)));
        Assert.Equal("en", MarketplaceLocalePolicy.Fallback);
    }
}
