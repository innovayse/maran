namespace Maran.Modules.Databases.Commands.CreateDatabase;

/// <summary>
/// Creates a MySQL database for an account, together with a dedicated user granted full privileges
/// on that database alone, and mints the password that user is created with (spec §11).
/// </summary>
/// <remarks>
/// The command carries no password, and none may be added: the panel generates one
/// (<c>ProvisionedPasswordGenerator</c>) rather than accepting one, so there is no customer-chosen
/// value to validate, to transport, or to find in a request log.
/// </remarks>
/// <param name="AccountId">The account that will own the database.</param>
/// <param name="Name">The database name the customer asked for, without the account prefix.</param>
/// <param name="DbUserName">
/// The dedicated user's name, without the account prefix. Chosen independently of
/// <paramref name="Name"/> because MySQL's two namespaces are independent and a customer may want
/// one user's name to say what it is for rather than to repeat the database's.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record CreateDatabaseCommand(
    Guid AccountId,
    string Name,
    string DbUserName,
    string IpAddress,
    string UserAgent);
