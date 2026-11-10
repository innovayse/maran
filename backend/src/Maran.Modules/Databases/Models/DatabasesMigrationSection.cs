namespace Maran.Modules.Databases.Models;

/// <summary>
/// What the Databases module hands to another server for one account: the databases and the names of their
/// logins, the additional database users with their grants, and no password (issue #127).
/// </summary>
/// <remarks>
/// The content of each database travels as a dump inside the account's backup. A login's password is never
/// part of a package: the import generates a new one and shows it once (decision D3).
/// </remarks>
/// <param name="Databases">The account's databases.</param>
/// <param name="Users">The account's additional database users, each with the privileges it holds.</param>
public sealed record DatabasesMigrationSection(
    IReadOnlyList<DatabaseMigrationItem> Databases,
    IReadOnlyList<DatabaseUserMigrationItem> Users);
