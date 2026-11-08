using System.Globalization;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Files.Commands.CompleteFileDownload;

/// <summary>Handles <see cref="CompleteFileDownloadCommand"/>: writes the download's completion entry.</summary>
/// <remarks>
/// The account is read through <see cref="IAccountDirectory"/>, the same tenant-scoped window every
/// operation uses, only to name the account in the entry: the download was already admitted when it was
/// opened, and a refusal here would not undo bytes already sent. The entry's subject is the account
/// and the path, as every entry of this module's, followed by the byte count the panel wrote.
/// </remarks>
public sealed class CompleteFileDownloadCommandHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="journal">This module's audit journal.</param>
    public CompleteFileDownloadCommandHandler(IAccountDirectory accounts, FilesAuditJournal journal)
    {
        _accounts = accounts;
        _journal = journal;
    }

    /// <summary>Records the completion.</summary>
    /// <param name="command">The validated parameters; see <see cref="CompleteFileDownloadCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the lookup; the entry itself is written regardless.</param>
    /// <returns>Success — or <c>AccountNotFound</c> when the account is no longer visible to the caller.</returns>
    public async Task<Result> HandleAsync(CompleteFileDownloadCommand command, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var bytes = command.BytesSent.ToString(CultureInfo.InvariantCulture);
        var subject = $"{FilesAuditJournal.SubjectOf(account.Username, command.Path)} ({bytes} bytes)";

        // A completion is a record of bytes that were sent, and a caller who has gone away does not unsend them.
        await _journal.RecordSuccessAsync(
            FileAuditActions.FileDownloadCompleted,
            account.Id,
            subject,
            new FilesCallOrigin(command.IpAddress, command.UserAgent),
            CancellationToken.None);

        return Result.Ok();
    }
}
