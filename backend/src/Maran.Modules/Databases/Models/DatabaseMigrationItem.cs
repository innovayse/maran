namespace Maran.Modules.Databases.Models;

/// <summary>One database of an account as it travels to another server: its name and the suffix of its login, never a password.</summary>
/// <param name="Name">The database's name within the account, without the account prefix.</param>
/// <param name="DbUserNameSuffix">The suffix of the database's own login within the account.</param>
public sealed record DatabaseMigrationItem(string Name, string DbUserNameSuffix);
