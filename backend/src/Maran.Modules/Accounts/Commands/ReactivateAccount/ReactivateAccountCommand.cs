namespace Maran.Modules.Accounts.Commands.ReactivateAccount;

/// <summary>Lifts a suspension, putting the account's sites and services back (spec §8).</summary>
/// <param name="AccountId">The account to reactivate.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ReactivateAccountCommand(Guid AccountId, string IpAddress, string UserAgent);
