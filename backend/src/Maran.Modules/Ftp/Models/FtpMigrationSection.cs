namespace Maran.Modules.Ftp.Models;

/// <summary>
/// What the Ftp module hands to another server for one account: the names of its FTPS logins, and no password
/// (issue #127).
/// </summary>
/// <param name="UserNames">The login names within the account, without the account prefix.</param>
public sealed record FtpMigrationSection(IReadOnlyList<string> UserNames);
