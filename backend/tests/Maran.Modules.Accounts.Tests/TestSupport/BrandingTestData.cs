namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>Named values for the branding tests: a real tiny image per accepted type, and colours either side of the contrast line.</summary>
public static class BrandingTestData
{
    /// <summary>A colour that carries white text (about 5.1:1).</summary>
    public const string DarkEnoughColor = "#2468dc";

    /// <summary>A colour too light for white text (well under 4.5:1).</summary>
    public const string TooLightColor = "#ffcc00";

    /// <summary>A data URI whose bytes begin with the PNG signature.</summary>
    public static readonly string PngLogo = DataUri("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]);

    /// <summary>A data URI whose bytes begin like a JPEG.</summary>
    public static readonly string JpegLogo = DataUri("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8]);

    /// <summary>A data URI whose bytes are a RIFF container holding WebP.</summary>
    public static readonly string WebpLogo = DataUri(
        "image/webp", [0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 1, 2]);

    /// <summary>Builds a base64 data URI.</summary>
    /// <param name="mediaType">The declared media type.</param>
    /// <param name="bytes">The payload.</param>
    /// <returns>The data URI.</returns>
    public static string DataUri(string mediaType, byte[] bytes)
    {
        return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
    }
}
