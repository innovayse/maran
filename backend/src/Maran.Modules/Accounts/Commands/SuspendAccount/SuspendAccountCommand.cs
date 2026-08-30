namespace Maran.Modules.Accounts.Commands.SuspendAccount;

/// <summary>Suspends a hosting account: its sites and services stop, its data stays (spec §8).</summary>
/// <param name="AccountId">The account to suspend.</param>
public sealed record SuspendAccountCommand(Guid AccountId);
