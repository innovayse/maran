namespace Maran.Sdk.Contracts;

/// <summary>
/// The facts one module needs about an account owned by another. Deliberately the smallest set that
/// answers "who is this on the host, and how many sites may it have" — not a copy of the account.
/// </summary>
/// <remarks>
/// A snapshot is read on the request path and used immediately; it is never stored. Denormalising
/// any of these fields into another module's table would create a second source of truth that goes
/// stale the moment the account is renamed or its plan changed (rules/architecture.md "Truth lives
/// in PostgreSQL").
/// </remarks>
/// <param name="Id">The account's identity, the same value a tenant-scoped row carries as its <c>AccountId</c>.</param>
/// <param name="Username">
/// The account's Linux system user name. Every agent operation on a customer's files, vhosts and
/// pools is addressed by this name, because the isolation between customers is the operating
/// system's.
/// </param>
/// <param name="MaxSites">
/// The plan's site allowance. Countable limits are enforced in the application at creation time
/// (spec §8), so the module creating the row is the module that must be able to read this.
/// </param>
/// <param name="MaxPhpWorkersPerPool">
/// The plan's php-fpm worker budget for ONE pool, written into each rendered pool as
/// <c>pm.max_children</c> (spec §8, §11). Per pool and not per account, matching
/// <c>Plan.MaxPhpWorkersPerPool</c> and the call site that passes it; the name says so because the
/// two readings differ by the number of PHP versions an account uses. It travels with the snapshot
/// because the module that re-renders a pool is the module that must supply it, and a fabricated
/// default here would be a customer silently given the wrong CPU ceiling.
/// </param>
public sealed record AccountSnapshot(Guid Id, string Username, int MaxSites, int MaxPhpWorkersPerPool);
