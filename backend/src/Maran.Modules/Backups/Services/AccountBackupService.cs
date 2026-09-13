using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// This module's implementation of <see cref="IAccountBackupService"/>: the final backup an account
/// gets before it is destroyed (spec §12).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the create path with two differences, and both of them are about the row outliving its
/// account.</b> The row's kind is <see cref="BackupKind.PreDeletion"/>, which is what exempts it
/// from this module's own account cascade and from retention; and the answer is a
/// <see cref="Result{T}"/> rather than a recorded outcome the caller reads at leisure, because the
/// caller has to be able to refuse the deletion on it.
/// </para>
/// <para>
/// <b>Why a failed run answers a FAILURE here and a completed row on the customer's own create
/// path.</b> <c>CreateBackupCommandHandler</c> deliberately answers success carrying a
/// <see cref="BackupStatus.Failed"/> row, because a screen has to show the failure either way and an
/// error would leave the caller holding a code and no id. Here the caller is not a screen: it is a
/// deletion deciding whether to proceed, and the only thing it can do with "the row says Failed" is
/// what a failed <see cref="Result{T}"/> already says. Folding it into the result is what makes the
/// refusal impossible to forget at the call site.
/// </para>
/// <para>
/// <b>The row is written before the agent is asked and the failure is recorded, not discarded.</b>
/// A refused deletion leaves a <see cref="BackupStatus.Failed"/> <see cref="BackupKind.PreDeletion"/>
/// row naming the account and the code — which is the record an operator needs in order to fix
/// whatever refused and delete the account afterwards. A run that left nothing behind would present
/// the operator with a deletion that fails for a reason nothing in the panel states.
/// </para>
/// <para>
/// <b>No panel task is opened, and that is deliberate.</b> The deletion has already opened one
/// (<c>TaskKinds.AccountDeletion</c>) and reports this step onto it; a second task for a step of the
/// first would give an operator two rows for one operation, which can disagree. The run's progress
/// is therefore reported onto the deletion's task, whose id the caller does not pass — so this step
/// contributes its own stage line through the deletion handler instead, and the runner is given
/// <see cref="Guid.Empty"/>, which <see cref="ITaskRecorder"/> defines as "there is no task".
/// </para>
/// </remarks>
public sealed class AccountBackupService : IAccountBackupService
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The stream consumer that reduces one run to a single outcome.</summary>
    private readonly BackupRunner _runner;

    /// <summary>Resolves which destination the final backup writes to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="runner">The stream consumer that drives the run.</param>
    /// <param name="destinations">Resolves which destination the final backup writes to.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the row.</param>
    public AccountBackupService(
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

    /// <inheritdoc />
    public async Task<Result<Guid>> TakeFinalBackupAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        // Resolved first, so a panel that cannot name a destination REFUSES the deletion instead of
        // recording a final backup that never happened. Every exit of this method is a fact the
        // deletion handler branches on, and "there was nowhere to put it" must be one of them.
        var destination = await _destinations.ResolveAsync(destinationId: null, cancellationToken);
        if (!destination.IsSuccess)
        {
            return Result<Guid>.Fail(destination.Error!);
        }

        var backup = new Backup(
            Guid.NewGuid(), accountId, destination.Value!.Id, BackupKind.PreDeletion, _clock.UtcNow);

        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var outcome = await _runner.RunAsync(
            accountName, backup.Id, destination.Value.Agent, Guid.Empty, cancellationToken);

        if (outcome.Succeeded)
        {
            backup.Completed(outcome.SizeBytes, outcome.Sha256, outcome.DatabaseCount, _clock.UtcNow);
        }
        else
        {
            backup.Failed(outcome.FailureCode, _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // The caller has no address to record — a deletion is journalled by the Accounts module
        // under its own action — so what this journal adds is the fact that the FINAL backup
        // specifically was attempted and how it ended, against the account being destroyed: the
        // one name that is left to search for once the deletion finishes.
        if (!outcome.Succeeded)
        {
            await _journal.RecordFailureAsync(
                AuditActions.FinalBackupTaken, accountName, string.Empty, string.Empty, cancellationToken);

            return Result<Guid>.Fail(Error.Of(nameof(ErrorMessages.FinalBackupFailed), ErrorType.Failure));
        }

        await _journal.RecordSuccessAsync(
            AuditActions.FinalBackupTaken, accountName, string.Empty, string.Empty, cancellationToken);

        return Result<Guid>.Ok(backup.Id);
    }
}
