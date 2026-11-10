namespace Maran.Modules.Sftp.Models;

/// <summary>One SFTP login of an account as it travels to another server: its name and public keys, never a password.</summary>
/// <param name="Name">The login's name within the account, without the account prefix.</param>
/// <param name="Keys">The login's authorized public keys.</param>
public sealed record SftpUserMigrationItem(string Name, IReadOnlyList<SftpKeyMigrationItem> Keys);
