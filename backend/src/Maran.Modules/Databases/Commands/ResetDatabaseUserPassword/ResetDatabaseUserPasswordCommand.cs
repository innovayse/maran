namespace Maran.Modules.Databases.Commands.ResetDatabaseUserPassword;

/// <summary>
/// Gives one database user a new password. The only recovery for a lost one, since nothing keeps a copy.
/// </summary>
/// <param name="UserId">Which user to re-credential. A row identifier; another tenant's answers "not found".</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ResetDatabaseUserPasswordCommand(Guid UserId, string IpAddress, string UserAgent);
