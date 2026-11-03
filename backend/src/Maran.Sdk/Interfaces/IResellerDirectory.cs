using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads the small set of reseller facts other modules need (<see cref="ResellerSnapshot"/>). The
/// contract lives in the Sdk and its implementation in the Accounts module, which owns the resellers
/// table — the same shape <see cref="IAccountDirectory"/> has, because a module may never reference
/// another module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// Read-only by design: a window onto another module's data, not a second way to change it. It
/// applies the caller's reseller scope, so a reseller reads only itself and an administrator reads
/// any reseller; a reseller asking for another's id gets the same <c>null</c> as for an id that does
/// not exist.
/// </remarks>
public interface IResellerDirectory
{
    /// <summary>Reads one reseller's snapshot, scoped to what the current user may see.</summary>
    /// <param name="resellerId">The reseller to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when no such reseller exists OR when it is another reseller's.
    /// The two cases are deliberately indistinguishable: telling them apart would let a caller
    /// confirm the existence of a tenant it may not see (rules/security.md — 404, never 403).
    /// </returns>
    Task<ResellerSnapshot?> FindAsync(Guid resellerId, CancellationToken cancellationToken);
}
