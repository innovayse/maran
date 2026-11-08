using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>Behavioral contract of <see cref="ResellerBranding"/>: it replaces all three values together.</summary>
public sealed class ResellerBrandingTests
{
    /// <summary>The instant of the first change.</summary>
    private static readonly DateTimeOffset First = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Creating the branding stores the three values and stamps the instant.</summary>
    [Fact]
    public void Creating_stores_the_three_values_and_the_instant()
    {
        var resellerId = Guid.NewGuid();

        var branding = new ResellerBranding(resellerId, "Acme", "#2468dc", BrandingTestData.PngLogo, First);

        Assert.Equal(resellerId, branding.ResellerId);
        Assert.Equal("Acme", branding.DisplayName);
        Assert.Equal("#2468dc", branding.AccentColor);
        Assert.Equal(BrandingTestData.PngLogo, branding.Logo);
        Assert.Equal(First, branding.UpdatedAt);
    }

    /// <summary>The colour is kept in lower case so two spellings of one colour are one value.</summary>
    [Fact]
    public void The_colour_is_stored_in_lower_case()
    {
        var branding = new ResellerBranding(Guid.NewGuid(), null, "#2468DC", null, First);

        Assert.Equal("#2468dc", branding.AccentColor);
    }

    /// <summary>Replacing sets every value, so one left null goes back to the panel's own.</summary>
    [Fact]
    public void Replacing_sets_every_value_and_a_null_clears_it()
    {
        var branding = new ResellerBranding(Guid.NewGuid(), "Acme", "#2468dc", BrandingTestData.PngLogo, First);
        var later = First.AddHours(1);

        branding.Replace("Beta", null, null, later);

        Assert.Equal("Beta", branding.DisplayName);
        Assert.Null(branding.AccentColor);
        Assert.Null(branding.Logo);
        Assert.Equal(later, branding.UpdatedAt);
    }
}
