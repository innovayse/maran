namespace Maran.Modules.Ftp.Commands.ResetFtpUserPassword;

/// <summary>
/// Gives one of the caller's FTPS logins a freshly minted password, and returns that value once.
/// </summary>
/// <remarks>
/// This is not a convenience. Nobody in this system keeps a copy of an FTPS password — not the
/// panel, not the agent — and creating the same login again is reported as already existing WITHOUT
/// touching the credential, precisely so that retrying a creation is safe. That makes this command
/// the only way a customer who lost their password ever connects again.
///
/// It carries no password of its own, for the reason <c>CreateFtpUserCommand</c> gives: the panel
/// mints the value rather than accepting one, so there is no customer-chosen secret to validate, to
/// transport, or to find in a request log.
/// </remarks>
/// <param name="FtpUserId">
/// Which login to re-credential. A row identifier and never a system user name: the row is what says
/// who owns the login, and another tenant's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ResetFtpUserPasswordCommand(Guid FtpUserId, string IpAddress, string UserAgent);
