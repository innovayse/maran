using System.Globalization;
using System.Resources;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Resources;

namespace Maran.Host.Demo.Tests.Resources;

/// <summary>Every sentence the guard can show exists in all three languages.</summary>
public sealed class GuardTextTests
{
    /// <summary>The languages the panel ships.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The resource family holding the area sentences.</summary>
    private static readonly ResourceManager Messages =
        new("Maran.Host.Demo.Resources.DemoMessages", typeof(DemoMessages).Assembly);

    /// <summary>The resource family holding the two codes.</summary>
    private static readonly ResourceManager Errors =
        new("Maran.Host.Demo.Resources.ErrorMessages", typeof(ErrorMessages).Assembly);

    /// <summary>Reads one entry from exactly one language's table, with no fallback to another.</summary>
    /// <param name="manager">The resource family.</param>
    /// <param name="culture">The language.</param>
    /// <param name="key">The entry.</param>
    /// <returns>The text, or <c>null</c> when that language's table lacks it.</returns>
    private static string? Own(ResourceManager manager, string culture, string key)
    {
        var info = culture == "en" ? CultureInfo.InvariantCulture : new CultureInfo(culture);
        return manager.GetResourceSet(info, createIfNotExists: true, tryParents: false)?.GetString(key);
    }

    /// <summary>Every refusal area has a non-empty sentence in en, ru and hy.</summary>
    [Fact]
    public void Every_refusal_area_has_a_sentence_in_every_language()
    {
        foreach (var area in DemoRefusalAreaIds.All)
        {
            foreach (var culture in Cultures)
            {
                var id = DemoRefusalAreaIds.IdOf(area);
                Assert.False(string.IsNullOrWhiteSpace(Own(Messages, culture, id)), $"{id} has no {culture} sentence.");
            }
        }
    }

    /// <summary>The codes the guard answers with have text in every language.</summary>
    [Theory]
    [InlineData("DemoActionRefused")]
    [InlineData("DemoBudgetExhausted")]
    [InlineData("DemoContentRefused")]
    public void The_guards_codes_have_a_sentence_in_every_language(string code)
    {
        foreach (var culture in Cultures)
        {
            Assert.False(string.IsNullOrWhiteSpace(Own(Errors, culture, code)), $"{code} has no {culture} sentence.");
        }
    }
}
