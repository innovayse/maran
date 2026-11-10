using Maran.Host.Modules;
using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Search;

/// <summary>
/// Decides whether a search provider may be asked at all: only when the module that owns it is composed
/// into the panel and is <see cref="ModuleState.Enabled"/>. The licence boundary of the search, enforced
/// on the server and not by the SPA hiding a group.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail closed on ownership.</b> A provider is attributed to a module by the assembly its type lives in, among the
/// modules this start COMPOSED (shipped and packaged alike), not the registry of shipped ones: a packaged
/// module's provider would otherwise never be asked. A provider that belongs to no composed module is not asked: it answers to nobody's licence, and asking
/// it would be the one way a module's data could be reached without the module's state being consulted.
/// </para>
/// <para>
/// <b>The licence is asked of the same window as everywhere else, and only for a paid module.</b> The state
/// of a module is <see cref="ModuleStateResolver"/> applied to its tier and the verdict of
/// <see cref="ILicenceEntitlements"/> (<see cref="VerdictsOfAsync"/>), so the search, the catalogue and the
/// request-time gate cannot disagree about whether a covered module is usable. A module that is
/// <see cref="LicenceTier.Included"/> consults no licence, so it stays searchable whatever the window says,
/// and a panel with no paid module never asks the window at all. A paid module the window does not name is
/// not covered.
/// </para>
/// </remarks>
public static class SearchModuleGate
{
    /// <summary>Reads the window's verdict for every paid module the panel composed, in one read.</summary>
    /// <param name="composition">What this start composed: the shipped modules and the packaged ones that passed.</param>
    /// <param name="entitlements">The licence window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The verdict by module id for each module whose tier is not <see cref="LicenceTier.Included"/>; empty
    /// when every composed module is included, in which case the window is not asked.
    /// </returns>
    public static async Task<IReadOnlyDictionary<string, EntitlementVerdict>> VerdictsOfAsync(
        PackagedModuleComposition composition,
        ILicenceEntitlements entitlements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(entitlements);

        var paidIds = composition.Composed
            .Where(module => { return module.Manifest.Tier != LicenceTier.Included; })
            .Select(module => { return module.Manifest.Id; })
            .ToList();

        if (paidIds.Count == 0)
        {
            return new Dictionary<string, EntitlementVerdict>(StringComparer.Ordinal);
        }

        return (await entitlements.ListAsync(paidIds, cancellationToken))
            .ToDictionary(
                entitlement => { return entitlement.ModuleId; },
                entitlement => { return entitlement.Verdict; },
                StringComparer.Ordinal);
    }

    /// <summary>Whether <paramref name="provider"/> may be asked.</summary>
    /// <param name="composition">What this start composed: the shipped modules and the packaged ones that passed.</param>
    /// <param name="verdicts">What <see cref="VerdictsOfAsync"/> read for the paid modules.</param>
    /// <param name="provider">The provider about to be called.</param>
    /// <returns><c>true</c> when its module is composed and enabled.</returns>
    public static bool Permits(
        PackagedModuleComposition composition,
        IReadOnlyDictionary<string, EntitlementVerdict> verdicts,
        ISearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(verdicts);
        ArgumentNullException.ThrowIfNull(provider);

        var assembly = provider.GetType().Assembly;
        var owner = composition.Composed.FirstOrDefault(module => { return module.GetType().Assembly == assembly; });
        if (owner is null)
        {
            return false;
        }

        var verdict = verdicts.GetValueOrDefault(owner.Manifest.Id, EntitlementVerdict.NotCovered);

        return ModuleStateResolver.Of(owner.Manifest.Tier, verdict) == ModuleState.Enabled;
    }
}
