namespace Maran.Modules.Accounts.Commands.SuspendAccount;

/// <summary>Suspends a hosting account: its sites and services stop, its data stays (spec §8).</summary>
/// <param name="AccountId">The account to suspend.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record SuspendAccountCommand(Guid AccountId, string IpAddress, string UserAgent);
