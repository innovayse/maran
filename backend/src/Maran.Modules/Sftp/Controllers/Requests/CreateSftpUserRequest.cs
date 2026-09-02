namespace Maran.Modules.Sftp.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/sftp-users</c>.</summary>
/// <remarks>
/// A separate type from the command: the command carries the caller's address and user agent, which
/// are read from the connection and must never be settable by the request that is being audited.
///
/// It has no password field, and none may be added. The panel mints the credential
/// (<c>ProvisionedPasswordGenerator</c>) from the alphabet the agent accepts, so a customer-chosen
/// value would be one more secret travelling inbound through request logging, proxies and browser
/// history for no gain.
///
/// It has no chroot path either. The jail is derived from the account by the agent, so the customer
/// has no directory to name and this request has no path to be trusted with.
/// </remarks>
/// <param name="AccountId">The account that will own the login.</param>
/// <param name="Name">The login name, without the account prefix; lowercase letters and digits.</param>
public sealed record CreateSftpUserRequest(Guid AccountId, string Name);
