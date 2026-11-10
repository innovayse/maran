namespace Maran.Modules.Sftp.Models;

/// <summary>
/// What the Sftp module hands to another server for one account: login names and their public keys, and no
/// password (issue #127).
/// </summary>
/// <remarks>
/// A public key authorizes a login without being a secret, so it travels and keeps the customer's key-based
/// access working. A login's password never does: the import generates a new one and shows it once.
/// </remarks>
/// <param name="Users">The account's SFTP logins.</param>
public sealed record SftpMigrationSection(IReadOnlyList<SftpUserMigrationItem> Users);
