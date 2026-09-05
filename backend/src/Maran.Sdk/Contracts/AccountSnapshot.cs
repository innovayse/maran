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
/// <param name="MaxDatabases">
/// The plan's database allowance, enforced by the Databases module at creation time and before the
/// agent is called (spec §8). Here for the same reason <paramref name="MaxSites"/> is: the module
/// that creates the row is the module that must be able to read the limit, and it may not reach into
/// the Accounts schema to find it.
/// </param>
/// <param name="MaxSftpUsers">
/// The plan's SFTP-login allowance, enforced by the Sftp module at creation time and before the
/// agent is called (spec §8). Here for the same reason <paramref name="MaxDatabases"/> is: the
/// module that creates the row is the module that must be able to read the limit, and it may not
/// reach into the Accounts schema to find it.
/// </param>
/// <param name="MaxCronEntries">
/// The plan's scheduled-task allowance, enforced by the Cron module at creation time and before the
/// agent is asked to install anything (spec §8). Here for the same reason
/// <paramref name="MaxSftpUsers"/> is: the module that creates the thing is the module that must be
/// able to read the limit, and it may not reach into the Accounts schema to find it. Unlike the
/// three above it is NOT counted against rows in the enforcing module's own tables — the Cron module
/// keeps none, because the account's crontab is the truth — so it is counted against what the agent
/// reports the crontab currently holds.
/// </param>
/// <param name="MaxPhpWorkersPerPool">
/// The plan's php-fpm worker budget for ONE pool, written into each rendered pool as
/// <c>pm.max_children</c> (spec §8, §11). Per pool and not per account, matching
/// <c>Plan.MaxPhpWorkersPerPool</c> and the call site that passes it; the name says so because the
/// two readings differ by the number of PHP versions an account uses. It travels with the snapshot
/// because the module that re-renders a pool is the module that must supply it, and a fabricated
/// default here would be a customer silently given the wrong CPU ceiling.
/// </param>
/// <param name="DiskQuotaMb">
/// The plan's disk allowance, in MEGABYTES — the unit <c>Plan.DiskQuotaMb</c> stores it in, carried
/// across unconverted so that the number here and the number an operator typed into the plan are the
/// same number. It is the one allowance on this record that is NOT enforced by a module counting its
/// own rows: nothing in the panel can stop a customer writing a file, so it is enforced on the
/// filesystem and merely REPORTED against here, by comparing it with what the agent measured.
///
/// The comparison is where the units meet: the agent reports bytes (<c>AccountDiskUsage.used_bytes</c>),
/// deliberately produces no quota of its own, and the reader that puts the two beside each other is
/// the one that must convert. Megabytes and not bytes here because a quota is the PANEL's own datum —
/// chosen when the account was created — and re-expressing it in the agent's unit at the boundary
/// would make the stored figure and the travelling figure two different numbers for one fact.
/// </param>
public sealed record AccountSnapshot(
    Guid Id,
    string Username,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    int DiskQuotaMb);
