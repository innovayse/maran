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
/// There is no check in the handler that the caller may set it, and that is deliberate rather than
/// an omission — the only construction site is <c>AccountsController</c>, whose every route is
/// behind <c>AuthorizationPolicies.AdminOnly</c> at class level. A structural guarantee beats a
/// remembered one, and adding a second check inside the handler would suggest there is some other
/// route in.
/// </param>
public sealed record DeleteAccountCommand(
    Guid AccountId,
    string IpAddress,
    string UserAgent,
    bool SkipFinalBackup = false);
