using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Policies;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Commands.SaveBackupDestination;

/// <summary>Records a new backup destination, or says why this build cannot.</summary>
/// <remarks>
/// <para>
/// <b>This is where a remote destination is refused, and it is the earlier of the two boundaries
/// that ask <see cref="RemoteDestinationPolicy"/>.</b> Refusing before the row is stored means no
/// remote destination can be created through the panel at all, so the refusal in
/// <see cref="BackupDestinationResolver"/> is left guarding only what could arrive another way — a
/// row written in <c>psql</c>, or by a later migration.
/// </para>
/// <para>
/// <b>A second LOCAL destination is refused too, and for the agent's reason rather than a policy of
/// ours.</b> The agent refuses a local destination that carries a path and writes to its own
/// constant root, so a second local row would be a row this panel could not honour — every backup
/// against it would land in the first one's directory while the panel said otherwise. It becomes
/// possible on the day the agent accepts a root, and the refusal names that state rather than
/// pretending the operator asked for something wrong.
/// </para>
/// <para>
/// Both outcomes are audited under the same action with <c>succeeded=false</c>, which is this tree's
/// idiom (one action name plus the flag), because "an operator tried to add an S3 destination" is
/// exactly the fact that says whether the remote arm is wanted.
/// </para>
/// </remarks>
public sealed class SaveBackupDestinationCommandHandler
{
    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="journal">This module's audit journal.</param>
    public SaveBackupDestinationCommandHandler(BackupAuditJournal journal)
    {
        _journal = journal;
    }

    /// <summary>Records the destination, or refuses with the reason this build cannot hold it.</summary>
    /// <param name="command">The validated request; see <see cref="SaveBackupDestinationCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <c>BackupDestinationRemoteUnsupported</c> for a remote destination this build cannot act on,
    /// or <c>BackupDestinationLocalAlreadyDefined</c> for a second local root the agent could not be
    /// told about.
    /// </returns>
    /// <remarks>
    /// It returns no success arm today, and the type still says <see cref="BackupDestinationDto"/>
    /// because that is what it will answer with — a handler whose return type were <c>Result</c> with
    /// no payload would have to change shape, and every caller with it, on the day the first
    /// destination can be stored.
    /// </remarks>
    public async Task<Result<BackupDestinationDto>> HandleAsync(
        SaveBackupDestinationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The subject is the label the administrator asked to record — the one thing about the
        // refused destination they can search for. No account is involved, and the entry's whole
        // point is which storage was asked for and refused.
        await _journal.RecordFailureAsync(
            AuditActions.BackupDestinationSaved,
            command.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        if (!RemoteDestinationPolicy.Admits(command.Kind))
        {
            return Result<BackupDestinationDto>.Fail(
                Error.Of(nameof(ErrorMessages.BackupDestinationRemoteUnsupported), ErrorType.Validation));
        }

        return Result<BackupDestinationDto>.Fail(
            Error.Of(nameof(ErrorMessages.BackupDestinationLocalAlreadyDefined), ErrorType.Conflict));
    }
}
