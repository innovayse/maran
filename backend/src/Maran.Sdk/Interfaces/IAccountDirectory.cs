using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads the small set of account facts other modules need (<see cref="AccountSnapshot"/>). The
/// contract lives in the Sdk and its implementation in the module that owns the accounts table, the
/// same shape <see cref="IAuditWriter"/> established — because a module may never reference another
/// module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// Read-only by design: this is a window onto another module's data, not a second way to change it.
/// A cross-module abstraction is precisely where isolation gets bypassed by accident — the filter
/// that protects the owning module's own queries does not reach through this interface on its own.
/// </para>
/// <para>
/// <b>The two methods differ on tenancy, and the difference is the first thing to read.</b>
/// <see cref="FindAsync"/> MUST apply the caller's tenant scope, answering <c>null</c> for an
/// account the current user does not own, exactly as a tenant-scoped query filter would.
/// <see cref="ListAsync"/> applies NONE and hands back every account on the host, because its one
/// use — the administrator's host-wide disk view — has no tenant to scope by. Its own remarks say
/// so at length; nothing may call it without gating itself to administrators first.
/// </para>
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

    /// <summary>
    /// Reads EVERY account on this host, applying NO tenant scope whatsoever.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// One snapshot per account, in no guaranteed order. Empty when the host has no accounts —
    /// never <c>null</c>, because "there are none" is an answer and this method has no way to say
    /// "you may not ask".
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This method deliberately breaks the promise the interface's own remarks make above.</b>
    /// <see cref="FindAsync"/> applies the caller's tenant scope and answers <c>null</c> for
    /// somebody else's account; this one applies nothing and answers with every account there is.
    /// It is stated here in full rather than left to be discovered, because the remarks that
    /// introduce this type say a cross-module abstraction is exactly where isolation gets bypassed
    /// by accident — and an unscoped method on a scoped interface is what that accident looks like.
    /// </para>
    /// <para>
    /// <b>It is unscoped because its one caller has no tenant to scope by.</b> It exists for the
    /// host-wide disk view: an administrator comparing what the agent measured under every home
    /// directory against what each account's plan allows. That reading is about the machine, and a
    /// version of it filtered to the signed-in user's own account would answer a question nobody
    /// asked while hiding the account that is actually full.
    /// </para>
    /// <para>
    /// <b>The whole authorization burden therefore sits on the caller, and there is no second
    /// line.</b> A customer who reached this would learn every other tenant's system user name — the
    /// name that addresses every agent operation — plus their plan's allowances. Anything reading it
    /// MUST be gated to administrators at its own boundary; today's one caller is behind
    /// <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c> on every route of its controller,
    /// and an integration test asserts a signed-in customer is refused on each of them.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken);
}
