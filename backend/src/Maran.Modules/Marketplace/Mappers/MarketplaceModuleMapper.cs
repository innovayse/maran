using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates a catalogue module, with the decisions already made about it, as the wire shape.</summary>
public static class MarketplaceModuleMapper
{
    /// <summary>Maps one catalogue module.</summary>
    /// <param name="module">The catalogue module.</param>
    /// <param name="assessment">Where it stands and whether its newest version can be staged, decided by <c>InstallEligibility</c>.</param>
    /// <param name="installedVersion">The installed version, or null when it is not installed.</param>
    /// <param name="restartRequired">Whether it is staged and waits for a restart.</param>
    /// <param name="texts">The words, in the caller's language.</param>
    /// <returns>The wire shape.</returns>
    public static MarketplaceModuleDto ToDto(
        CatalogueModule module,
        ModuleAssessment assessment,
        string? installedVersion,
        bool restartRequired,
        ModuleTexts texts)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(texts);

        return new MarketplaceModuleDto(
            module.Id,
            module.DisplayName,
            module.Summary,
            module.Publisher,
            module.Tier,
            texts.TierDisplayName,
            module.Price is null ? null : new MarketplacePriceDto(module.Price.Display, module.Price.PurchaseUrl),
            module.Latest?.Version,
            installedVersion,
            assessment.State,
            new MarketplaceEntitlementDto(module.Entitlement.State, texts.EntitlementName, module.Entitlement.Until),
            assessment.Refusal is null,
            texts.Reason,
            restartRequired);
    }
}
