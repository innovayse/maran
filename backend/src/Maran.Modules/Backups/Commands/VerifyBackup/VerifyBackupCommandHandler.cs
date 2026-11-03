using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.VerifyBackup;

/// <summary>
/// Handles <see cref="VerifyBackupCommand"/>: has the agent read one stored artifact back and answers
/// with the backup as the panel now records it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A finding is an answer, not an error.</b> An artifact found damaged is a successful
/// verification whose result is a failed state, so the caller receives the backup row saying so, with
/// its code and its localized wording — exactly as a backup that failed to run is returned by the
/// create endpoint rather than as a bare failure. Only a verification that COULD NOT RUN is returned
/// as an error, with the agent's own reason, and it leaves the recorded state exactly as it was: a
/// busy agent is not a corrupt backup.
/// </para>
/// <para>
/// <b>Another account's backup is not-found, never forbidden.</b> Nothing here checks the account:
/// the context's global query filter hides the row, so the lookup finds nothing and the agent is
/// never asked. The account name and the backup id the agent receives are read from the row, never
/// from the request.
/// </para>
/// <para>
/// <b>Only a completed backup has an artifact to verify.</b> A running one is being written at this
/// moment and a failed one has none.
/// </para>
/// </remarks>
public sealed class VerifyBackupCommandHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Asks the agent, records the state and journals the result.</summary>
    private readonly BackupVerifier _verifier;

    /// <summary>This module's audit journal, for the refusals that never reach the verifier.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>Names a recorded failure code in the caller's language.</summary>
    private readonly BackupFailureDisplayNames _failureNames;

    /// <summary>Names a backup's status, kind and verification state in the caller's language.</summary>
    private readonly BackupDisplayNames _backupNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="verifier">Asks the agent, records the state and journals the result.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="failureNames">Names a recorded failure code in the caller's language.</param>
    /// <param name="backupNames">Names a backup's status, kind and verification state in the caller's language.</param>
    public VerifyBackupCommandHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        BackupVerifier verifier,
        BackupAuditJournal journal,
        BackupFailureDisplayNames failureNames,
        BackupDisplayNames backupNames)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _verifier = verifier;
        _journal = journal;
        _failureNames = failureNames;
        _backupNames = backupNames;
    }

    /// <summary>Verifies one backup's artifact and answers with the backup as now recorded.</summary>
    /// <param name="command">The backup to verify.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The backup, whatever the verification found — or <c>BackupNotFound</c>,
    /// <c>BackupNotVerifiable</c>, <c>AccountNotFound</c>, or the reason the agent could not perform
    /// it.
    /// </returns>
    public async Task<Result<BackupDto>> HandleAsync(
        VerifyBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var backup = await _dbContext.Backups
            .FirstOrDefaultAsync(row => row.Id == command.BackupId, cancellationToken);
        if (backup is null)
        {
            // No row is visible to this caller, so no account can be named: the trace records the
            // identifier that was probed for, exactly so the probe stays searchable.
            return await FailAsync(
                command,
                command.BackupId.ToString(),
                Error.Of(nameof(ErrorMessages.BackupNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        if (backup.Status != BackupStatus.Completed)
        {
            return await FailAsync(
                command,
                command.BackupId.ToString(),
                Error.Of(nameof(ErrorMessages.BackupNotVerifiable), ErrorType.Conflict),
                cancellationToken);
        }

        var username = await ResolveAccountUsernameAsync(backup, cancellationToken);
        if (username is null)
        {
            return await FailAsync(
                command,
                command.BackupId.ToString(),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var outcome = await _verifier.VerifyAsync(
            backup, username, command.IpAddress, command.UserAgent, cancellationToken);
        if (outcome.NotPerformed is not null)
        {
            return Result<BackupDto>.Fail(outcome.NotPerformed);
        }

        // Asked of the directory exactly as the reads ask it, so this answer and a later read of the
        // same row cannot label its owner differently.
        var ownerNames = await _accounts.NamesOfAsync([backup.AccountId], cancellationToken);

        return Result<BackupDto>.Ok(BackupMapper.From(
            backup,
            ownerNames,
            _failureNames.Of(backup.RunFailureCode),
            _backupNames.Of(backup.Status),
            _backupNames.Of(backup.Kind),
            _backupNames.Of(backup.VerificationState),
            _failureNames.Of(backup.VerificationFailureCode)));
    }

    /// <summary>The system user name whose directory this backup's archive sits in.</summary>
    /// <param name="backup">The backup being verified.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The user name, or <c>null</c> when the account cannot be named at all.</returns>
    /// <remarks>
    /// The row first, the directory second, for the reason the deletion gives: a backup kept past its
    /// account's deletion can only be named from the row, and verifying the last copy of a deleted
    /// customer's data is exactly when an administrator wants to.
    /// </remarks>
    private async Task<string?> ResolveAccountUsernameAsync(
        Backup backup,
        CancellationToken cancellationToken)
    {
        if (backup.DeletedAccountUsername is { } deletedAccountUsername)
        {
            return deletedAccountUsername;
        }

        var account = await _accounts.FindAsync(backup.AccountId, cancellationToken);

        return account?.Username;
    }

    /// <summary>Journals a refused verification and returns it as the typed failure.</summary>
    /// <param name="command">The verification that was refused.</param>
    /// <param name="subject">
    /// What the refusal is recorded against: the identifier the caller acted on, because no account
    /// could be named, so a probe for a backup the caller may not see still leaves a trace.
    /// </param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    /// <remarks>
    /// The one funnel every refusal passes through, so no early return can leave the journal
    /// unwritten.
    /// </remarks>
    private async Task<Result<BackupDto>> FailAsync(
        VerifyBackupCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            BackupAuditActions.BackupVerified, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<BackupDto>.Fail(error);
    }
}
