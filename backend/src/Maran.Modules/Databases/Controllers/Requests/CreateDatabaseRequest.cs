namespace Maran.Modules.Databases.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/databases</c>.</summary>
/// <remarks>
/// A separate type from the command: the command carries the caller's address and user agent, which
/// are read from the connection and must never be settable by the request that is being audited.
///
/// It has no password field, and none may be added. The panel mints the credential
/// (<c>ProvisionedPasswordGenerator</c>) from the alphabet the agent accepts, so a customer-chosen
/// value would be one more secret travelling inbound through request logging, proxies and browser
/// history for no gain.
/// </remarks>
/// <param name="AccountId">The account that will own the database.</param>
/// <param name="Name">The database name, without the account prefix; lowercase letters and digits.</param>
/// <param name="DbUserName">The dedicated user's name, without the account prefix; likewise.</param>
public sealed record CreateDatabaseRequest(Guid AccountId, string Name, string DbUserName);
