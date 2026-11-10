namespace Maran.Modules.Sftp.Models;

/// <summary>One authorized public key of an SFTP login: public material, so it may travel.</summary>
/// <param name="Label">The key's label.</param>
/// <param name="PublicKey">The public key line.</param>
public sealed record SftpKeyMigrationItem(string Label, string PublicKey);
