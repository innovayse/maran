namespace Maran.Modules.Sftp.Queries.GetSftpUser;

/// <summary>Reads one SFTP login.</summary>
/// <param name="SftpUserId">The login to read; another tenant's id answers "not found".</param>
public sealed record GetSftpUserQuery(Guid SftpUserId);
