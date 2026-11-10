using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Finds the version an install or update names in the verified catalogue and reads the panel-side facts
/// to judge it against, so the two commands that start from the catalogue share one front half.
/// </summary>
public sealed class ModuleOfferResolver
{
    /// <summary>The marketplace client.</summary>
    private readonly IMarketplaceClient _client;

    /// <summary>The eligibility rules, which read the panel-side facts.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>Creates the resolver.</summary>
    /// <param name="client">The marketplace client.</param>
    /// <param name="eligibility">The eligibility rules, which read the panel-side facts.</param>
    public ModuleOfferResolver(IMarketplaceClient client, InstallEligibility eligibility)
    {
        _client = client;
        _eligibility = eligibility;
    }

    /// <summary>Reads the module's details and picks the version asked for.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The exact version asked for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The offer, or the client's coded failure (switched off, unreachable, unverified, not in the catalogue).</returns>
    public async Task<Result<ResolvedOffer>> ResolveAsync(string moduleId, string version, CancellationToken ct)
    {
        var details = await _client.GetModuleAsync(moduleId, MarketplaceDisplayNames.Locale, ct);
        if (!details.IsSuccess)
        {
            return Result<ResolvedOffer>.Fail(details.Error!);
        }

        var asked = details.Value.Versions.FirstOrDefault(candidate =>
        {
            return string.Equals(candidate.Version, version, StringComparison.Ordinal);
        });
        var context = await _eligibility.LoadContextAsync(ct);

        return Result<ResolvedOffer>.Ok(new ResolvedOffer(details.Value.Module, asked, context));
    }
}
