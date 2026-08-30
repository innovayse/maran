namespace Maran.Modules.Accounts.Commands.ReactivateAccount;

/// <summary>Lifts a suspension, putting the account's sites and services back (spec §8).</summary>
/// <param name="AccountId">The account to reactivate.</param>
public sealed record ReactivateAccountCommand(Guid AccountId);
