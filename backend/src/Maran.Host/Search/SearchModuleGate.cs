using Maran.Host.Modules;
using Maran.Host.Modules.Enums;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Search;

/// <summary>
/// Decides whether a search provider may be asked at all: only when the module that owns it is composed
/// into the panel and is <see cref="ModuleState.Enabled"/>. The licence boundary of the search, enforced
/// on the server and not by the SPA hiding a group.
/// </summary>
/// <remarks>
/// <b>Fail closed.</b> A provider is attributed to a module by the assembly its type lives in, among the
/// modules this start COMPOSED (shipped and packaged alike), not the registry of shipped ones: a packaged
/// module's provider would otherwise never be asked. A provider that belongs to no composed module is not asked: it answers to nobody's licence, and asking
/// it would be the one way a module's data could be reached without the module's state being consulted.
/// </remarks>
public static class SearchModuleGate
{
    /// <summary>Whether <paramref name="provider"/> may be asked.</summary>
    /// <param name="composition">What this start composed: the shipped modules and the packaged ones that passed.</param>
    /// <param name="provider">The provider about to be called.</param>
    /// <returns><c>true</c> when its module is composed and enabled.</returns>
    public static bool Permits(PackagedModuleComposition composition, ISearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(provider);

        var assembly = provider.GetType().Assembly;
        var owner = composition.Composed.FirstOrDefault(module => { return module.GetType().Assembly == assembly; });

        return owner is not null && ModuleStateResolver.Of(owner.Manifest.Tier) == ModuleState.Enabled;
    }
}
