namespace Maran.Modules.Databases.Models;

/// <summary>
/// An additional database user of an account and what it may do, as a package carries it (issue #127): the login
/// name without the account prefix and its grants, and no password.
/// </summary>
/// <param name="NameSuffix">The login name without the account prefix.</param>
/// <param name="Grants">The databases the user holds privileges on; empty for a user granted nothing.</param>
public sealed record DatabaseUserMigrationItem(string NameSuffix, IReadOnlyList<DatabaseUserGrantMigrationItem> Grants);
