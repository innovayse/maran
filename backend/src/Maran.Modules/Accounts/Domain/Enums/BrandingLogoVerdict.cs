namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>What <see cref="Policies.BrandingLogoPolicy"/> decided about a submitted logo.</summary>
public enum BrandingLogoVerdict
{
    /// <summary>The logo is a PNG, JPEG or WebP image inside the size limit, carried as a base64 data URI.</summary>
    Acceptable,

    /// <summary>The value is not a base64 data URI of a PNG, JPEG or WebP image whose bytes begin like one.</summary>
    NotAnImage,

    /// <summary>The value is a well-formed image data URI longer than <see cref="Entities.ResellerBranding.LogoMaxLength"/>.</summary>
    TooLarge,
}
