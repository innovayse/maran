namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Removes a hosting account, its system user and everything it owns on disk.</summary>
/// <param name="AccountId">The account to remove.</param>
public sealed record DeleteAccountCommand(Guid AccountId);
