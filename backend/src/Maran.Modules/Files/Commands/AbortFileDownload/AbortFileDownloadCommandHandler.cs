using Maran.Modules.Files.Models;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Files.Commands.AbortFileDownload;

/// <summary>Handles <see cref="AbortFileDownloadCommand"/>: writes the download's aborted entry.</summary>
/// <remarks>
/// The account is read through <see cref="IAccountDirectory"/>, the same tenant-scoped window every
/// operation uses, only to name the account in the entry: the download was already admitted when it was
/// opened, and a refusal here would not undo the transfer that died. The entry's subject is the account and
/// the path, as every entry of this module's. It is a failure entry, like an aborted upload's.
/// </remarks>
public sealed class AbortFileDownloadCommandHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="journal">This module's audit journal.</param>
    public AbortFileDownloadCommandHandler(IAccountDirectory accounts, FilesAuditJournal journal)
    {
        _accounts = accounts;
        _journal = journal;
    }

    /// <summary>Records the abort.</summary>
    /// <param name="command">The validated parameters; see <see cref="AbortFileDownloadCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the lookup; the entry itself is written regardless.</param>
    /// <returns>Success — or <c>AccountNotFound</c> when the account is no longer visible to the caller.</returns>
    public async Task<Result> HandleAsync(AbortFileDownloadCommand command, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        // The usual reason is a caller who has gone away, and that is exactly when this record is wanted.
        await _journal.RecordFailureAsync(
            FileAuditActions.FileDownloadAborted,
            account.Id,
            FilesAuditJournal.SubjectOf(account.Username, command.Path),
            new FilesCallOrigin(command.IpAddress, command.UserAgent),
            CancellationToken.None);

        return Result.Ok();
    }
}
