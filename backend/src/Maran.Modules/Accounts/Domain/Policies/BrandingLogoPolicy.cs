using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// The rule about which logo a reseller may give its customers' shell: a small PNG, JPEG or WebP image,
/// carried as a base64 data URI whose bytes really begin like the image type it claims.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not SVG.</b> An SVG document can carry script and external references, and a logo shown to a
/// reseller's customers is content one tenant puts in front of another's users. The three raster
/// formats cannot execute anything; the panel's content-security policy already allows
/// <c>data:</c> images and nothing from outside, which is also why the logo is stored and served as a
/// data URI and not as a link to a host the customer's browser would have to trust.
/// </para>
/// <para>
/// <b>Why the bytes are checked and not the label.</b> The declared type is text the caller wrote; the
/// first bytes of the decoded image are what a browser sniffs. A value that declares PNG and begins like
/// a script is refused.
/// </para>
/// </remarks>
public static class BrandingLogoPolicy
{
    /// <summary>The prefix every accepted value starts with, up to the media type.</summary>
    private const string Prefix = "data:";

    /// <summary>The marker between the media type and the payload.</summary>
    private const string Base64Marker = ";base64,";

    /// <summary>The PNG file signature.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The first three bytes of every JPEG file.</summary>
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>The first four bytes of a WebP file's RIFF container.</summary>
    private static readonly byte[] RiffSignature = [0x52, 0x49, 0x46, 0x46];

    /// <summary>The four bytes at offset eight that make the RIFF container a WebP.</summary>
    private static readonly byte[] WebpSignature = [0x57, 0x45, 0x42, 0x50];

    /// <summary>Decides whether a submitted logo may be stored.</summary>
    /// <param name="logo">The submitted data URI.</param>
    /// <returns>Whether it is acceptable, not an accepted image, or too large.</returns>
    public static BrandingLogoVerdict Check(string logo)
    {
        ArgumentNullException.ThrowIfNull(logo);

        if (!logo.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return BrandingLogoVerdict.NotAnImage;
        }

        var markerAt = logo.IndexOf(Base64Marker, StringComparison.Ordinal);
        if (markerAt < 0)
        {
            return BrandingLogoVerdict.NotAnImage;
        }

        var mediaType = logo.Substring(Prefix.Length, markerAt - Prefix.Length);
        if (mediaType is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            return BrandingLogoVerdict.NotAnImage;
        }

        if (logo.Length > ResellerBranding.LogoMaxLength)
        {
            return BrandingLogoVerdict.TooLarge;
        }

        var payload = logo.AsSpan(markerAt + Base64Marker.Length);
        var buffer = new byte[(payload.Length / 4 * 3) + 3];
        if (!Convert.TryFromBase64Chars(payload, buffer, out var written) || written < WebpSignature.Length + 8)
        {
            return BrandingLogoVerdict.NotAnImage;
        }

        var bytes = buffer.AsSpan(0, written);
        var matches = mediaType switch
        {
            "image/png" => bytes.StartsWith(PngSignature),
            "image/jpeg" => bytes.StartsWith(JpegSignature),
            _ => bytes.StartsWith(RiffSignature) && bytes.Slice(8, 4).SequenceEqual(WebpSignature),
        };

        return matches ? BrandingLogoVerdict.Acceptable : BrandingLogoVerdict.NotAnImage;
    }
}
