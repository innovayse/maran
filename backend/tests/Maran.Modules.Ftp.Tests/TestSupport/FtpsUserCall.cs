namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// How one login call addressed the agent, captured by <see cref="RecordingFtpsAgent"/> so a test
/// can assert the two VALUES that decide which tenant's login is touched.
/// </summary>
/// <remarks>
/// Both halves matter and neither is decoration. The agent applies the account prefix itself and
/// checks the candidate's passwd home against that account's jail, so a call carrying the wrong
/// account, or carrying a fully-qualified name instead of a suffix, is the shape a cross-tenant
/// operation would take. Recording the pair is what lets a test say the panel sent the row's own
/// account and the customer's bare suffix.
/// </remarks>
/// <param name="AccountUsername">The owning account's system user name the panel sent.</param>
/// <param name="FtpsUsername">The login name suffix the panel sent, without any prefix.</param>
public sealed record FtpsUserCall(string AccountUsername, string FtpsUsername);
