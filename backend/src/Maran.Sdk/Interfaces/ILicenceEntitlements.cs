using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The read-only window every module, filter and background job asks "does the licence cover this
/// module?". Implemented by the Licensing module, which owns the licence file, the lease and the rule;
/// consumers see a verdict and never the licence (rules/architecture.md "A shared facility's contract
/// lives in Maran.Sdk"). The credential accessor of the module loader (<see cref="ILicenceLease"/>) is
/// a different window by design and answers its coverage question from the same rule.
/// </summary>
/// <remarks>
/// Implementations never throw: any failure to read the licence answers
/// <see cref="EntitlementVerdict.NotCovered"/>, so a paid module fails closed while the rest of the
/// panel, which never asks, is untouched. Included modules do not ask.
/// </remarks>
public interface ILicenceEntitlements
{
    /// <summary>Answers the verdict for one module.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The module's entitlement; never null.</returns>
    Task<ModuleEntitlement> GetAsync(string moduleId, CancellationToken cancellationToken);

    /// <summary>Answers the verdict for several modules with one read of the licence.</summary>
    /// <param name="moduleIds">The modules' ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entitlement per requested id, in the requested order.</returns>
    Task<IReadOnlyList<ModuleEntitlement>> ListAsync(
        IReadOnlyCollection<string> moduleIds,
        CancellationToken cancellationToken);
}
