namespace Maran.Modules.Sftp.Commands.DeleteSftpUser;

/// <summary>
/// Removes one of the caller's SFTP logins, and only the login.
/// </summary>
/// <remarks>
/// The account's files are NOT touched. The login's passwd home is the jail, the account's real home
/// is bind-mounted inside it, and removing a login means revoking a key rather than deleting what it
/// opened — so unlike dropping a database, nothing here destroys customer data.
/// </remarks>
/// <param name="SftpUserId">
/// Which login to remove. A row identifier and never a system user name: the row is what says who
/// owns the login, and another tenant's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteSftpUserCommand(Guid SftpUserId, string IpAddress, string UserAgent);
