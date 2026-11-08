using Maran.Modules.Files.Models;
using Maran.Modules.Files.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Files.Services;

/// <summary>
/// The one question every Files handler asks first: may the caller touch this account's files at all,
/// and as which system user.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tenancy.</b> The account is resolved ONLY through <see cref="IAccountDirectory.FindAsync"/>, which
/// is tenant-filtered and fail-closed: another account's id is null, and the answer is
/// <c>AccountNotFound</c> with <see cref="ErrorType.NotFound"/> (404, never 403 — a 403 would confirm
/// the account exists). Nothing is sent to the agent, and a failure entry naming the probed id is
/// journalled. The agent is addressed by the snapshot's <c>Username</c>, never by a name taken from the
/// request.
/// </para>
/// <para>
/// <b>Suspension.</b> A customer or sub-user caller is refused with <c>FilesAccountSuspended</c>
/// (<see cref="ErrorType.Conflict"/>); an administrator may still act. The check lives here, in one
/// place, and a handler that skips this call is caught by a test that walks every handler with a
/// suspended account.
/// </para>
/// <para>
/// <b>What it does not do.</b> The plan feature and a sub-user's grant are asked by the controller's
/// <c>[RequiresAccountFeature(AccountFeature.Files)]</c> before the request reaches a handler; this
/// gate does not ask them a second time. View-as is the controller's <c>[ViewAsAccess]</c>.
/// </para>
/// </remarks>
public sealed class FilesAccountGate
{
    /// <summary>The one window onto an account's system user name and state.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The authenticated principal, to tell an administrator from a customer.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Creates the gate.</summary>
    /// <param name="accounts">The account directory.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="journal">This module's audit journal.</param>
    public FilesAccountGate(IAccountDirectory accounts, ICurrentUser currentUser, FilesAuditJournal journal)
    {
        _accounts = accounts;
        _currentUser = currentUser;
        _journal = journal;
    }

    /// <summary>Resolves the account the caller may act on, or refuses and journals the refusal.</summary>
    /// <param name="accountId">The account named in the request.</param>
    /// <param name="action">
    /// The audit action to record a refusal under: the operation's own for a mutation, and
    /// <see cref="FileAuditActions.FileAccessRefused"/> for a read.
    /// </param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The account's snapshot — or <c>AccountNotFound</c> or <c>FilesAccountSuspended</c>.</returns>
    public async Task<Result<AccountSnapshot>> AdmitAsync(
        Guid accountId,
        string action,
        FilesCallOrigin origin,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            // The write does not take the request's token: a refusal is a record, and a caller who has
            // gone away does not un-refuse it. The account is not named as known: the caller may not see it.
            await _journal.RecordFailureAsync(
                action, null, accountId.ToString(), origin, CancellationToken.None);
            return Result<AccountSnapshot>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        if (account.State == AccountState.Suspended && _currentUser.Caller is not AdministratorCaller)
        {
            await _journal.RecordFailureAsync(
                action, account.Id, FilesAuditJournal.SubjectOf(account.Username, string.Empty), origin, CancellationToken.None);
            return Result<AccountSnapshot>.Fail(
                Error.Of(nameof(ErrorMessages.FilesAccountSuspended), ErrorType.Conflict));
        }

        return Result<AccountSnapshot>.Ok(account);
    }
}
