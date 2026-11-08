using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>What <see cref="BrandingLogoPolicy"/> accepts as the logo of a reseller's customers' shell.</summary>
public sealed class BrandingLogoPolicyTests
{
    /// <summary>A PNG, a JPEG and a WebP whose bytes match their declared type are accepted: the accepting control.</summary>
    /// <param name="kind">Which fixture.</param>
    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public void An_image_whose_bytes_match_its_type_is_accepted(string kind)
    {
        var logo = kind switch
        {
            "png" => BrandingTestData.PngLogo,
            "jpeg" => BrandingTestData.JpegLogo,
            _ => BrandingTestData.WebpLogo,
        };

        Assert.Equal(BrandingLogoVerdict.Acceptable, BrandingLogoPolicy.Check(logo));
    }

    /// <summary>A scripting image type, a link and a plain string are refused as not an image.</summary>
    /// <param name="logo">The value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/logo.png")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:text/html;base64,PGh0bWw+")]
    [InlineData("data:image/png,rawbytes")]
    [InlineData("data:image/png;base64,@@@@")]
    public void A_value_that_is_not_a_base64_png_jpeg_or_webp_is_refused(string logo)
    {
        Assert.Equal(BrandingLogoVerdict.NotAnImage, BrandingLogoPolicy.Check(logo));
    }

    /// <summary>The bytes are checked, not the label: a script declared as a PNG is refused.</summary>
    [Fact]
    public void A_payload_that_does_not_begin_like_the_declared_type_is_refused()
    {
        var script = System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>");

        Assert.Equal(
            BrandingLogoVerdict.NotAnImage,
            BrandingLogoPolicy.Check(BrandingTestData.DataUri("image/png", script)));
        Assert.Equal(
            BrandingLogoVerdict.NotAnImage,
            BrandingLogoPolicy.Check(BrandingTestData.DataUri("image/webp", [0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0, 1, 2, 3, 4, 5])));
    }

    /// <summary>A well-formed image over the length limit is refused as too large, one at the limit is accepted.</summary>
    [Fact]
    public void An_image_over_the_length_limit_is_refused_as_too_large()
    {
        var padding = new byte[ResellerBranding.LogoMaxLength];
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var bytes = png.Concat(padding).ToArray();

        Assert.Equal(
            BrandingLogoVerdict.TooLarge,
            BrandingLogoPolicy.Check(BrandingTestData.DataUri("image/png", bytes)));
        Assert.Equal(BrandingLogoVerdict.Acceptable, BrandingLogoPolicy.Check(BrandingTestData.PngLogo));
    }
}
