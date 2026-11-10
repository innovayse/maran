namespace Maran.Modules.Databases.Models;

/// <summary>One grant of an additional database user, as a package carries it (issue #127).</summary>
/// <param name="DatabaseName">The database's name without the account prefix, as the account's own list shows it.</param>
/// <param name="Privileges">The names of the <c>DatabasePrivilege</c> members the user holds on it, each at most once.</param>
public sealed record DatabaseUserGrantMigrationItem(string DatabaseName, IReadOnlyList<string> Privileges);
