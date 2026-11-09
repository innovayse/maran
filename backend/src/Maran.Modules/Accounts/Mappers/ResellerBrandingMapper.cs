using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>Restates a reseller's stored branding as the wire shape the shell and the settings screen read.</summary>
public static class ResellerBrandingMapper
{
    /// <summary>Projects the stored branding, or the absence of one, onto the wire shape.</summary>
    /// <param name="branding">The stored branding, or <c>null</c> when the reseller set none (or the caller has none).</param>
    /// <returns>The branding, with every member <c>null</c> when there is none.</returns>
    public static ResellerBrandingDto ToDto(ResellerBranding? branding)
    {
        return branding is null
            ? new ResellerBrandingDto(null, null, null)
            : new ResellerBrandingDto(branding.DisplayName, branding.AccentColor, branding.Logo);
    }
}
