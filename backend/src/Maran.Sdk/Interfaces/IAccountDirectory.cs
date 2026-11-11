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
/// <b>The methods differ on tenancy, and the difference is the first thing to read.</b>
/// <see cref="FindAsync"/>, <see cref="NamesOfAsync"/> and <see cref="FindIdsByNameAsync"/> answer
/// the question "which accounts may the caller's TENANT DATA touch": they MUST apply the caller's
/// tenant scope, answering nothing for an account the current user does not own, exactly as a
/// tenant-scoped query filter would. A reseller is a login that owns accounts and has no account
/// of its own, so those three answer it NOTHING — it reaches a customer's resources only through a
/// view-as session, whose token is a customer's (issue #179). <see cref="FindForViewAsAsync"/> asks
/// a different question — which accounts may this caller START a view-as session on — and is the
/// one place a reseller's own accounts are answered.
/// <see cref="ListAsync"/> applies NONE and hands back every account on the host, because its one
/// use — the administrator's host-wide disk view — has no tenant to scope by. Its own remarks say
/// so at length; nothing may call it without gating itself to administrators first.
/// <see cref="FindSystemUserAsync"/> also applies NONE, answers one account's system user name by id
/// to every caller alike, and is for unattended work only; it is never reached from a request path.
/// <see cref="FindForSystemAsync"/> applies NONE either and answers one account's whole snapshot by id, for
/// system actions raised after the owning module authorised them.
/// </para>
/// </remarks>
public interface IAccountDirectory
{
    /// <summary>
    /// Reads one account's snapshot, scoped to the accounts the current user's TENANT DATA may touch: an
    /// administrator any account, a customer the one they own, and nobody else — a reseller included.
    /// </summary>
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when no such account exists OR when it belongs to another
    /// tenant. The two cases are deliberately indistinguishable: telling them apart would let a
    /// caller confirm the existence of an account it may not see (rules/security.md — 404, never 403).
    /// </returns>
    Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one account's snapshot for the single purpose of starting a view-as session on it: an
    /// administrator may start one on any account, a reseller only on an account it owns, and no other
    /// caller on any.
    /// </summary>
    /// <param name="accountId">The account the operator asks to view as.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when no such account exists OR when it is outside the caller's
    /// view-as scope. The two cases are indistinguishable, as <see cref="FindAsync"/> requires.
    /// </returns>
    /// <remarks>
    /// This is NOT a second tenant scope for data. It exists because "which accounts may the caller's
    /// data touch" (<see cref="FindAsync"/>: a reseller touches none) and "which accounts may the caller
    /// look through as their owner" (a reseller's own) are two questions, and answering both with one
    /// method either let a reseller read a customer's resources outside a view-as session or refused it
    /// the view-as itself. Only the view-as start asks this one; a module serving a tenant's data never
    /// does.
    /// </remarks>
    Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken);

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
    /// <b>It is unscoped because its callers have no tenant to scope by.</b> It serves one
    /// administrator-only route, the host-wide disk view: an administrator comparing what the agent
    /// measured under every home directory against what each account's plan allows. That reading is
    /// about the machine, and a version of it filtered to the signed-in user's own account would
    /// answer a question nobody asked while hiding the account that is actually full. It also serves
    /// unattended work that runs with no principal at all: the backup run, backup retention, backup
    /// reclamation at start-up and the PHP site reconcile at start-up.
    /// </para>
    /// <para>
    /// <b>The whole authorization burden therefore sits on the caller, and there is no second
    /// line.</b> A customer who reached this would learn every other tenant's system user name — the
    /// name that addresses every agent operation — plus their plan's allowances. Anything reading it
    /// MUST be gated to administrators at its own boundary or run with no request at all; the
    /// one route that reads it is behind
    /// <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c> on every route of its controller,
    /// and an integration test asserts a signed-in customer is refused on each of them.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the names of several accounts in one round-trip, scoped to what the current user's tenant data
    /// may touch.
    /// </summary>
    /// <param name="accountIds">The accounts whose names to read; duplicates are harmless.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Account id to account name, for every requested account the caller may see (none, for a reseller).
    /// An id that does not exist and an id belonging to another tenant are both simply absent, for the
    /// reason <see cref="FindAsync"/> gives: the two must be indistinguishable.
    /// </returns>
    /// <remarks>
    /// The batched form exists so that a list naming the owner of each of its rows asks once per
    /// page, not once per row. Scoped exactly as <see cref="FindAsync"/> is.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds every account whose name contains <paramref name="search"/>, case-insensitively and
    /// literally, scoped to what the current user's tenant data may touch.
    /// </summary>
    /// <param name="search">
    /// The term, matched literally: <c>%</c>, <c>_</c> and <c>\</c> carry no pattern meaning.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The id of EVERY matching account, in no guaranteed order. Another tenant's account never
    /// appears, so a customer can match only their own and a reseller none.
    /// </returns>
    /// <remarks>
    /// This is how another module searches its own rows by owner name without reading this module's
    /// tables: it asks for the matching ids here and filters its rows by them. The answer is not
    /// capped, because a cap would make the caller's search silently miss the rows of every account
    /// past it. That is affordable because the answer is one column of the accounts on one server,
    /// and the caller sends it back to PostgreSQL as a single array parameter
    /// (<c>= ANY(@ids)</c>, which is how Npgsql translates <c>Contains</c> over a list), not as one
    /// parameter per id.
    /// </remarks>
    Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one account's snapshot, applying NO tenant scope and answering every caller alike. For SYSTEM
    /// actions raised after the owning module authorised them: it must never be reached to serve a request's
    /// own data.
    /// </summary>
    /// <param name="accountId">
    /// The account to read, by id only. The caller must hold the id from an event or a row of its own, never
    /// from a request.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The snapshot, or <c>null</c> when no such account exists.</returns>
    /// <remarks>
    /// <para>
    /// <b>This member deliberately breaks the scoping promise of <see cref="FindAsync"/>.</b> A system action
    /// (binding a reactivated account's mail, applying a plan change) acts on the account whoever asked for the
    /// action, and a reseller is answered nothing by <see cref="FindAsync"/> for an account it legitimately
    /// manages. Anything that returned this snapshot to a caller would disclose an account and its plan to
    /// someone the tenant scope excludes (an IDOR with a 200 in place of a 404, rules/security.md item 6), so
    /// the figures are used by the action and never handed back.
    /// </para>
    /// <para>
    /// Unlike <see cref="ListAsync"/> it reads exactly one row by key. Callers are listed by
    /// <c>grep -rnE "FindForSystemAsync" backend/src --include=*.cs</c> and fixed by a census test.
    /// </para>
    /// </remarks>
    Task<AccountSnapshot?> FindForSystemAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one account's system user name, applying NO tenant scope and answering every caller alike.
    /// For unattended work only: it must never be reached from a request path.
    /// </summary>
    /// <param name="accountId">
    /// The account to read, by id only. The caller must hold the id from its own row, never from a
    /// request.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The account's id and system user name, or <c>null</c> when no such account exists. Nothing
    /// else about the account is returned, and the member cannot list.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This member deliberately breaks the scoping promise of <see cref="FindAsync"/>.</b> Unattended
    /// work (certificate renewal, the hourly site log maintenance) runs with no principal, so
    /// <see cref="FindAsync"/> answers it nothing
    /// by design; this member answers the same for an anonymous caller, an administrator and a customer
    /// of another account. A handler on a request path that passed it an id a caller supplied would
    /// disclose whether that account exists and its system user name — an IDOR with a 200 in place of a
    /// 404 (rules/security.md item 6).
    /// </para>
    /// <para>
    /// Its only legitimate caller is unattended work that holds the id from its own row. Callers are
    /// listed by <c>grep -rnE "FindSystemUserAsync" backend/src --include=*.cs</c>.
    /// </para>
    /// </remarks>
    Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken);
}
