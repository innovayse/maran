namespace Maran.Modules.Databases.Commands.DropDatabaseUser;

/// <summary>Drops one additional database user, and every privilege it holds. Nothing here is recoverable.</summary>
/// <param name="UserId">
/// Which user to drop. A row identifier and never a MySQL name: the row says who owns the login, and
/// another tenant's identifier answers "not found". Built by the controller from the route; the command
/// is not bound from a request.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DropDatabaseUserCommand(Guid UserId, string IpAddress, string UserAgent);
