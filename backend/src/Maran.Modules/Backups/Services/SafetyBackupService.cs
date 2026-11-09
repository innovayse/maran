using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Takes the backup that is a safety net for a destructive operation and answers whether it exists:
/// the one path behind <see cref="BackupKind.PreDeletion"/> and <see cref="BackupKind.PreRestore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One path, because the two kinds share every decision and differ only in three words.</b> Both
/// write the row before the agent is asked, both run through <see cref="BackupRunner"/>, both turn a
/// run that produced nothing into a typed failure the caller must refuse its own operation on, and
/// both leave a failed row behind as the record of what refused. What differs is the kind stamped on
/// the row, the audit action the attempt is journalled under, and the code a failure answers with —
/// so those are the parameters, and a fix to the shared part is made once.
/// </para>
/// <para>
/// <b>The caller decides what a failure means for its operation.</b> This service never refuses
/// anything itself: it reports whether a usable archive exists. A deletion abandons itself on a
/// failure, and a restore refuses itself — in both the account is left exactly as it was.
/// </para>
/// <para>
/// <b>No panel task is opened.</b> Each caller already has, or deliberately has not yet opened, a
/// task of its own; a second task for one step of an operation would give an operator two rows for
/// one thing that can disagree. The runner is handed the task id the caller passes, and
/// <see cref="Guid.Empty"/> is "there is no task" (<see cref="Maran.Sdk.Interfaces.ITaskRecorder"/>).
/// </para>
/// </remarks>
public sealed class SafetyBackupService
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The stream consumer that reduces one run to a single outcome.</summary>
    private readonly BackupRunner _runner;

    /// <summary>Resolves which destination the safety backup writes to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="runner">The stream consumer that drives the run.</param>
    /// <param name="destinations">Resolves which destination the safety backup writes to.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the row.</param>
    public SafetyBackupService(
        BackupsDbContext dbContext,
        BackupRunner runner,
        BackupDestinationResolver destinations,
        BackupAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _runner = runner;
        _destinations = destinations;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Takes one safety backup of an account and reports whether a usable archive exists.</summary>
    /// <param name="kind">
    /// Why the copy is taken: <see cref="BackupKind.PreDeletion"/> or <see cref="BackupKind.PreRestore"/>.
    /// It is the answer to "may retention remove this", and it is recorded at creation and never changes.
    /// </param>
    /// <param name="accountId">The account being backed up.</param>
    /// <param name="accountName">The account's system user name.</param>
    /// <param name="auditAction">The audit action the attempt is journalled under, on success and failure alike.</param>
    /// <param name="failureCode">
    /// The code a run that produced nothing answers with. Its KIND is
    /// <see cref="ErrorType.Failure"/>: the server could not make the copy.
    /// </param>
    /// <param name="taskId">The panel task the run's progress is reported onto, or <see cref="Guid.Empty"/> for none.</param>
    /// <param name="ipAddress">The caller's address for the audit entry; empty when the caller has none.</param>
    /// <param name="userAgent">The caller's user agent for the audit entry; empty when the caller has none.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The identifier of the completed backup, or a typed failure.</returns>
    public async Task<Result<Guid>> TakeAsync(
        BackupKind kind,
        Guid accountId,
        string accountName,
        string auditAction,
        string failureCode,
        Guid taskId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        // Resolved first, so a panel that cannot name a destination REFUSES the operation instead of
        // recording a safety backup that never happened. Every exit of this method is a fact the
        // caller branches on, and "there was nowhere to put it" must be one of them.
        var destination = await _destinations.ResolveAsync(destinationId: null, cancellationToken);
        if (!destination.IsSuccess)
        {
            return Result<Guid>.Fail(destination.Error!);
        }

        var backup = new Backup(Guid.NewGuid(), accountId, destination.Value!.Id, kind, _clock.UtcNow);

        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var outcome = await _runner.RunAsync(
            accountName, backup.Id, destination.Value.AgentForNewBackup, taskId, cancellationToken);

        switch (outcome)
        {
            case CreatedBackupOutcome created:
                backup.Completed(created.SizeBytes, created.Sha256, created.DatabaseCount, _clock.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _journal.RecordSuccessAsync(auditAction, accountName, ipAddress, userAgent, cancellationToken);

                // The caller has what it asked for either way: a finding about the archive is
                // recorded and mailed, never turned into a refusal of the operation.
                await _runner.VerifyCompletedAsync(backup, accountName, cancellationToken);
                break;

            case FailedBackupOutcome failed:
                backup.Failed(failed.FailureCode, _clock.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _journal.RecordFailureAsync(auditAction, accountName, ipAddress, userAgent, cancellationToken);

                return Result<Guid>.Fail(Error.Of(failureCode, ErrorType.Failure));

            default:
                throw new InvalidOperationException(
                    $"Unhandled backup outcome {outcome.GetType().Name}; the hierarchy is closed to two cases.");
        }

        return Result<Guid>.Ok(backup.Id);
    }
}
