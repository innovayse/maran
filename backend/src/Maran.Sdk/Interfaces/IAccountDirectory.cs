using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads the small set of account facts other modules need (<see cref="AccountSnapshot"/>). The
/// contract lives in the Sdk and its implementation in the module that owns the accounts table, the
/// same shape <see cref="IAuditWriter"/> established — because a module may never reference another
/// module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// Read-only by design: this is a window onto another module's data, not a second way to change it.
/// An implementation MUST apply the caller's tenant scope, answering <c>null</c> for an account the
/// current user does not own, exactly as a tenant-scoped query filter would. A cross-module
/// abstraction is precisely where isolation gets bypassed by accident — the filter that protects
/// the owning module's own queries does not reach through this interface on its own.
/// </remarks>
public interface IAccountDirectory
{
    /// <summary>Reads one account's snapshot, scoped to what the current user may see.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when no such account exists OR when it belongs to another
    /// tenant. The two cases are deliberately indistinguishable: telling them apart would let a
    /// caller confirm the existence of an account it may not see (rules/security.md — 404, never 403).
    /// </returns>
    Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken);
}
