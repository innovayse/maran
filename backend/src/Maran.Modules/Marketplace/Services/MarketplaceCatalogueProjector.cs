using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Turns verified catalogue answers into the wire shapes the screen shows: it asks <see cref="InstallEligibility"/>
/// what each module and version may do, resolves the words, and hands both to the mappers. It decides
/// nothing itself.
/// </summary>
public sealed class MarketplaceCatalogueProjector
{
    /// <summary>The eligibility rules.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>The words, in the caller's language.</summary>
    private readonly MarketplaceDisplayNames _names;

    /// <summary>Creates the projector.</summary>
    /// <param name="eligibility">The eligibility rules.</param>
    /// <param name="names">The words, in the caller's language.</param>
    public MarketplaceCatalogueProjector(InstallEligibility eligibility, MarketplaceDisplayNames names)
    {
        _eligibility = eligibility;
        _names = names;
    }

    /// <summary>Projects one catalogue module.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The module as the screen shows it.</returns>
    public async Task<MarketplaceModuleDto> ProjectModuleAsync(
        EligibilityContext context,
        CatalogueModule module,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var assessment = await _eligibility.AssessAsync(context, module, module.Latest, ct);
        var shown = module.Price is null
            ? module
            : module with { Price = module.Price with { PurchaseUrl = PurchaseLinkPolicy.HttpsOnly(module.Price.PurchaseUrl) } };
        var texts = new ModuleTexts(
            _names.TierName(module.Tier),
            _names.EntitlementName(module.Entitlement.State),
            _names.SentenceOrNull(assessment.Refusal));

        InstalledModuleFacts? installed = InstallEligibility.IsInstalled(context, module.Id)
            ? context.Installed[module.Id]
            : null;

        return MarketplaceModuleMapper.ToDto(
            shown,
            assessment,
            installed?.Loaded.Version,
            installed is not null && InstalledModuleReader.ActionsOf(installed).RestartRequired,
            texts);
    }

    /// <summary>Projects every version of a module with whether each can be installed now.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="versions">The versions the service offers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The versions as the screen shows them, in the order given.</returns>
    public async Task<IReadOnlyList<MarketplaceVersionDto>> ProjectVersionsAsync(
        EligibilityContext context,
        CatalogueModule module,
        IReadOnlyList<CatalogueVersion> versions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(versions);

        var projected = new List<MarketplaceVersionDto>(versions.Count);
        foreach (var version in versions)
        {
            var assessment = await _eligibility.AssessAsync(context, module, version, ct);
            projected.Add(MarketplaceVersionMapper.ToDto(
                version, assessment.Refusal is null, _names.SentenceOrNull(assessment.Refusal)));
        }

        return projected;
    }

    /// <summary>Projects a module's signed manifest summary.</summary>
    /// <param name="manifest">The summary the service sent, or null.</param>
    /// <returns>The summary as the install dialog shows it, or null when the service sent none.</returns>
    public ModuleManifestSummaryDto? ProjectManifest(CatalogueManifestSummary? manifest)
    {
        return manifest is null
            ? null
            : ModuleManifestSummaryMapper.ToDto(manifest, _names.TierName(manifest.Tier));
    }
}
