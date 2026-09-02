namespace Maran.Modules.Databases.Commands.ResetDatabasePassword;

/// <summary>
/// Gives one of the caller's databases a freshly minted password for its dedicated user, and returns
/// that value once.
/// </summary>
/// <remarks>
/// This is not a convenience. Nobody in this system keeps a copy of a database password — not the
/// panel, not the agent — and creating the same database again is reported as already existing
/// WITHOUT touching the existing credential, precisely so that retrying a creation is safe. That
/// makes this command the only way a customer who lost their password ever connects again.
///
/// It carries no password of its own, for the reason <c>CreateDatabaseCommand</c> gives: the panel
/// mints the value rather than accepting one, so there is no customer-chosen secret to validate, to
/// transport, or to find in a request log.
/// </remarks>
/// <param name="DatabaseId">
/// Which database's user to re-credential. A row identifier and never a MySQL name: the row is what
/// says who owns the database, and another tenant's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ResetDatabasePasswordCommand(Guid DatabaseId, string IpAddress, string UserAgent);
