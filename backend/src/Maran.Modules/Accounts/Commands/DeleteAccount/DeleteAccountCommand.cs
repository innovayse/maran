namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Removes a hosting account, its system user and everything it owns on disk.</summary>
/// <param name="AccountId">The account to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
/// <param name="SkipFinalBackup">
/// Whether to delete without taking the final backup spec §12 promises. Default <c>false</c>, and
/// the default is the safe one: a deletion that took no copy is the one thing about this operation
/// that cannot be undone at all.
///
/// Only an administrator may set it, and the handler enforces that from the caller's own token: a
/// reseller shares the controller that constructs this command (<c>AccountsController</c> is open to
/// both), so the controller's policy alone no longer says who is asking, and a reseller who sets it is
/// refused <c>AccountFinalBackupSkipForbidden</c> before anything is looked up. A customer reaches no
/// route that builds this command at all.
/// </param>
public sealed record DeleteAccountCommand(
    Guid AccountId,
    string IpAddress,
    string UserAgent,
    bool SkipFinalBackup = false);
