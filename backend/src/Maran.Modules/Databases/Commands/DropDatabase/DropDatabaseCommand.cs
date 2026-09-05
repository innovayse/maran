namespace Maran.Modules.Databases.Commands.DropDatabase;

/// <summary>
/// Drops one of the caller's databases and the dedicated user it was created with. The customer's
/// data in it goes with it; nothing here is recoverable.
/// </summary>
/// <param name="DatabaseId">
/// Which database to drop. A row identifier and never a MySQL name: the row is what says who owns
/// the database, and another tenant's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DropDatabaseCommand(Guid DatabaseId, string IpAddress, string UserAgent);
