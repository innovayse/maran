using Maran.Modules.Backups.Domain.Policies;
using Maran.Modules.Backups.Interfaces;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Commands.SetBackupDestinationEncryption;

/// <summary>Switches a destination's archive encryption on or off, minting its key the first time.</summary>
/// <remarks>
/// <para>
/// <b>The key is minted here, by the panel, and is never returned.</b> The handler answers a bare
/// <see cref="Result"/>: a response that carried the key would put it in a browser, a proxy log and the
/// idempotency store, and nobody has a use for seeing it — it is consumed by the panel itself, sent to
/// the agent over the local socket and kept in the database under the panel's cipher
/// (rules/security.md item 8). An operator who needs to carry it somewhere carries the panel's own
/// recovery archive, which holds the database and the key that decrypts it.
/// </para>
/// <para>
/// <b>An existing key is reused, never replaced.</b> Switching off and on again does not mint a new
/// one (<c>BackupDestination.EnableArchiveEncryption</c>), so backups sealed in between stay
/// readable.
/// </para>
/// <para>
/// Both outcomes are audited under one action with the <c>succeeded</c> flag, which is this module's
/// idiom. The entry names the destination by its label and records neither the key nor whether one
/// exists: the fact an operator searches for is that the protection of the archives was changed, and
/// by whom.
/// </para>
/// </remarks>
public sealed class SetBackupDestinationEncryptionCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Makes the destination's key the first time encryption is switched on.</summary>
    private readonly IArchiveKeyGenerator _keys;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="keys">Makes the destination's key the first time encryption is switched on.</param>
    /// <param name="journal">This module's audit journal.</param>
    public SetBackupDestinationEncryptionCommandHandler(
        BackupsDbContext dbContext,
        IArchiveKeyGenerator keys,
        BackupAuditJournal journal)
    {
        _dbContext = dbContext;
        _keys = keys;
        _journal = journal;
    }

    /// <summary>Applies the setting, or says why the destination cannot take it.</summary>
    /// <param name="command">The validated request; see <see cref="SetBackupDestinationEncryptionCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success; <c>BackupDestinationNotFound</c> for an id that names no destination; or
    /// <c>BackupDestinationRemoteUnsupported</c> for a destination of a kind this build cannot write,
    /// whose archives the agent would refuse to seal anywhere.
    /// </returns>
    public async Task<Result> HandleAsync(
        SetBackupDestinationEncryptionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var destination = await _dbContext.BackupDestinations
            .FirstOrDefaultAsync(row => row.Id == command.DestinationId, cancellationToken);

        if (destination is null)
        {
            await _journal.RecordFailureAsync(
                BackupAuditActions.BackupDestinationEncryptionChanged,
                command.DestinationId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result.Fail(Error.Of(nameof(ErrorMessages.BackupDestinationNotFound), ErrorType.NotFound));
        }

        if (!RemoteDestinationPolicy.Admits(destination.Kind))
        {
            await _journal.RecordFailureAsync(
                BackupAuditActions.BackupDestinationEncryptionChanged,
                destination.Name,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result.Fail(
                Error.Of(nameof(ErrorMessages.BackupDestinationRemoteUnsupported), ErrorType.Validation));
        }

        if (command.Enabled)
        {
            destination.EnableArchiveEncryption(destination.ArchiveKey ?? _keys.Mint());
        }
        else
        {
            destination.DisableArchiveEncryption();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            BackupAuditActions.BackupDestinationEncryptionChanged,
            destination.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }
}
