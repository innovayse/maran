namespace Maran.Modules.Backups.Common;

/// <summary>
/// The parts of an account a restore can be narrowed to, as the panel's own rows name them (issue #160).
/// </summary>
/// <remarks>
/// The restore dialog selects from these lists instead of asking a person to type an identifier: a
/// screen that needs a database name or a domain gets the set from the backend (rules/architecture.md
/// "The backend owns the data, the SPA renders it"). The lists are the ACCOUNT'S current databases and
/// sites, not what the archive holds — the panel does not read inside an archive — so a name listed
/// here can still be refused by the agent as not in the backup. That refusal is the authority; this
/// list is advice about what is worth asking.
/// </remarks>
/// <param name="Databases">The full, account-prefixed names of the account's databases, in ascending order.</param>
/// <param name="Sites">The primary domains of the account's sites, in ascending order.</param>
public sealed record RestoreChoicesDto(IReadOnlyList<string> Databases, IReadOnlyList<string> Sites);
