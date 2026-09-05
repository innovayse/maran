namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Removes a hosting account, its system user and everything it owns on disk.</summary>
/// <param name="AccountId">The account to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteAccountCommand(Guid AccountId, string IpAddress, string UserAgent);
