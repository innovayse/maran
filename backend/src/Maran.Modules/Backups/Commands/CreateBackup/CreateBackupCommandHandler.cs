using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.CreateBackup;

/// <summary>
/// Handles <see cref="CreateBackupCommand"/>: records that a backup of the account has begun, has
/// the agent make it, and writes what the run produced — the row, the panel task and the audit entry
/// all from one outcome (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>The row is written BEFORE the agent is called, and it is written Running.</b> The reverse — a
/// row written from the terminal event only — loses the whole run whenever the panel dies in the
/// middle of it, and what is left behind is an archive on the destination that no row owns:
/// invisible to the interface, invisible to retention, and counted against nothing. A
/// <see cref="BackupStatus.Running"/> row that outlives its process is the recoverable failure, and
/// it is recoverable precisely because it is visible.
/// </para>
/// <para>
/// <b>The task and the row are written from the SAME terminal outcome.</b> Not from two readings of
/// the stream, and not from "the call returned without throwing". That is what makes a completed
/// task over a failed row unrepresentable rather than merely unlikely — the shape of defect the
/// account-deletion cascade shipped once, reporting COMPLETED at 100 over work it had not done.
/// </para>
/// <para>
/// <b>Tenancy is the account directory's answer, not a check here.</b>
/// <see cref="IAccountDirectory.FindAsync"/> is tenant-scoped, so an account this caller does not
/// own is <c>null</c> and the answer is not-found. An administrator sees every account, which is
/// what lets an operator take a backup on a customer's behalf.
/// </para>
/// </remarks>
public sealed class CreateBackupCommandHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The stream consumer that reduces one run to a single outcome.</summary>
    private readonly BackupRunner _runner;

    /// <summary>Resolves which destination a run writes to, and refuses one this build cannot use.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The panel-wide task journal, so an operator can watch a run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The current request's correlation id, recorded on the task beside its stages.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Names a recorded failure code in the caller's language.</summary>
    private readonly BackupFailureDisplayNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="runner">The stream consumer that drives the run.</param>
    /// <param name="destinations">Resolves which destination this run writes to.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the row.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    /// <param name="failureNames">Names a recorded failure code in the caller's language.</param>
    public CreateBackupCommandHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        BackupRunner runner,
        BackupDestinationResolver destinations,
        BackupAuditJournal journal,
        IClock clock,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        BackupFailureDisplayNames failureNames)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _runner = runner;
        _destinations = destinations;
        _journal = journal;
        _clock = clock;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _failureNames = failureNames;
    }

    /// <summary>Takes a backup of one account and records what it produced.</summary>
    /// <param name="command">The validated account; see <see cref="CreateBackupCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded backup — completed or failed, and the caller is told which by the row's status
    /// rather than by an error — or <c>AccountNotFound</c> when the account is not one the caller
    /// may back up.
    /// </returns>
    /// <remarks>
    /// A run that the agent could not finish is deliberately NOT returned as a failed
    /// <see cref="Result{T}"/>. The row exists, it says <see cref="BackupStatus.Failed"/> and it
    /// carries the code, and a screen has to show it either way; answering with an error instead
    /// would leave the caller holding a code and no id, unable to find the row the panel just wrote.
    /// The audit entry records the failure, which is where an operator looks.
    /// </remarks>
    public async Task<Result<BackupDto>> HandleAsync(
        CreateBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            // No account can be named for this caller, so the trace records the identifier that
            // was probed for — the journal's contract for a subject nobody could establish.
            await _journal.RecordFailureAsync(
                AuditActions.BackupCreated,
                command.AccountId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<BackupDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        // Resolved before the row is written, so the row records the destination the run actually
        // used rather than the one the panel happened to be configured with afterwards — and so a
        // panel with no usable destination refuses by name instead of writing a Running row it can
        // never move.
        var destination = await _destinations.ResolveAsync(destinationId: null, cancellationToken);
        if (!destination.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                AuditActions.BackupCreated, account.Username, command.IpAddress, command.UserAgent, cancellationToken);

            return Result<BackupDto>.Fail(destination.Error!);
        }

        var backup = new Backup(
            Guid.NewGuid(), command.AccountId, destination.Value!.Id, BackupKind.Manual, _clock.UtcNow);

        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The subject is the account, as every other kind of task names one: ITaskRecorder.BeginAsync
        // asks for "what the operation acts on, as an operator would search for it — a domain, an
        // account name", and a backup id satisfied neither half. It read as a bare UUID on the tasks
        // screen while the restore beside it named the account, and nothing joins a task to a backup
        // row by that string in any case — the correlation id is what lines a task up with the
        // request that started it.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.BackupCreate, account.Username, _correlationIds.CorrelationId, cancellationToken);

        BackupRunOutcome outcome;
        try
        {
            outcome = await _runner.RunAsync(
                account.Username, backup.Id, destination.Value.Agent, taskId, cancellationToken);
        }
        catch
        {
            // Every way the answer can fail to arrive, not a list of them. A cancelled request
            // raises one exception type, a torn gRPC stream another, and a module has no business
            // naming the transport's; what they have in common is the only thing this branch acts
            // on — the run's outcome was not observed, and the row must say so rather than stay
            // Running. Rethrown unchanged, so nothing is swallowed.
            await AbandonAsync(backup, account.Username, command, taskId);

            throw;
        }

        if (outcome.Succeeded)
        {
            backup.Completed(outcome.SizeBytes, outcome.Sha256, outcome.DatabaseCount, _clock.UtcNow);
        }
        else
        {
            backup.Failed(outcome.FailureCode, _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Both written from `outcome`, the one answer the run produced — never from the row being
        // re-read, and never from "nothing threw".
        if (outcome.Succeeded)
        {
            // The account, not the backup id — the same subject the task above records, and the one
            // AuditEntry asks for: what the operation acts on, as an operator would search for it.
            await _journal.RecordSuccessAsync(
                AuditActions.BackupCreated, account.Username, command.IpAddress, command.UserAgent, cancellationToken);
            await _tasks.CompleteAsync(taskId, cancellationToken);
        }
        else
        {
            await _journal.RecordFailureAsync(
                AuditActions.BackupCreated, account.Username, command.IpAddress, command.UserAgent, cancellationToken);
            await _tasks.FailAsync(taskId, outcome.FailureCode, cancellationToken);
        }

        return Result<BackupDto>.Ok(BackupMapper.From(backup, _failureNames.Of(backup.FailureCode)));
    }

    /// <summary>Closes out a run whose answer this request will never hear.</summary>
    /// <param name="backup">The row this request wrote Running before calling the agent.</param>
    /// <param name="username">The account the run was for, which every record here is subject to.</param>
    /// <param name="command">The command, for the address the attempt came from.</param>
    /// <param name="taskId">The panel task opened for the run.</param>
    /// <returns>Resolves once the row, the audit entry and the task have been written.</returns>
    /// <remarks>
    /// <para>
    /// <b>A call the panel can no longer hear is not a call that did not happen.</b> The agent's
    /// work is a detached <c>spawn_blocking</c>: dropping the stream cancels nothing, so the archive
    /// is very likely finished on the destination while this request is unwinding. What is lost is
    /// only the panel's knowledge of it, and the two ways that loss reaches the panel are ordinary —
    /// the shipped nginx closes an upstream at <c>proxy_read_timeout 300s</c>, and a customer who
    /// closes the tab aborts the request outright.
    /// </para>
    /// <para>
    /// <b>Without this the row stays Running for ever, and a Running row is not inert.</b> Both
    /// writers of a terminal status live downstream of the agent call, so an unwound request left
    /// one behind that nothing would ever move: it refuses every future restore of that account
    /// (<c>RestoreBackupCommandHandler</c>'s running-backup precondition), refuses its own deletion
    /// (<c>Backup.MayBeDeleted</c>), and is invisible to retention, so the archive's bytes are never
    /// reclaimed either. One timed-out backup therefore cost the customer the ability to restore
    /// from any of their good ones, with a manual database update as the only remedy.
    /// </para>
    /// <para>
    /// <b>Failed, and with its own code, rather than Completed.</b> The panel never learned the size
    /// or the digest, and a Completed row without a digest is a backup no restore can verify — the
    /// same reason <c>BackupRunner</c> refuses an artifact the agent could not name. The code says
    /// what is true, which is that the outcome was not observed, and it is distinct from every code
    /// meaning the run failed, so an operator can tell the two apart in the tasks feed.
    /// </para>
    /// <para>
    /// <b><see cref="CancellationToken.None"/> throughout, deliberately.</b> The request's token is
    /// already cancelled on the path that reaches here; passing it would abandon the write that
    /// exists precisely because the request was abandoned.
    /// </para>
    /// <para>
    /// <b>This runs only while the API process is alive, and that other half is now covered
    /// elsewhere.</b> A run whose process is killed or restarted mid-stream never reaches this
    /// <c>catch</c> at all, so it used to leave a Running row that nothing moved.
    /// <see cref="StartupBackupReconciler"/> is what moves it now, at the next start: it asks the
    /// destination through <c>IAgentBackupClient.ListAsync</c> what artifacts exist and closes the
    /// row on the answer. What this path still owns is the row it loses INSIDE a living process,
    /// where the answer is known without asking anybody.
    /// </para>
    /// <para>
    /// <b>The ARCHIVE this row stops describing is still not reclaimed, and that is deliberate.</b>
    /// The run finishes, so an artifact this row now calls a failure is very likely sitting on the
    /// destination — and a Failed row is invisible to retention (<c>Backup.MayBeRetentionPruned</c>
    /// admits only Completed ones), so nothing unattended will ever free those bytes. They are not
    /// unreachable: a Failed row MAY be deleted, and <c>DeleteBackupCommandHandler</c> removes the
    /// artifact through the agent before the row. Nothing here deletes customer bytes on the strength
    /// of a stream it lost. The startup reconciler closes the same gap in visibility rather than in
    /// disk space: when it finds such an archive it says so, by backup id, at warning level, and
    /// records the row under a code whose text tells the reader a copy exists that the panel cannot
    /// verify — so the leak is no longer silent even though it is still an operator's to release.
    /// </para>
    /// <para>
    /// <b>Why a lost stream is never COMPLETED from the destination's own listing, on either
    /// path.</b> The listing carries the artifact's size and its SHA-256, so a row could be completed
    /// from it — and must not be. <see cref="Backup.Sha256"/> is the panel's independently recorded
    /// digest precisely so that a restore does not trust a digest read from beside the bytes it
    /// describes, and a row completed from the sidecar would be indistinguishable from one the panel
    /// observed. So both paths fail the row; neither invents a provenance.
    /// </para>
    /// </remarks>
    private async Task AbandonAsync(Backup backup, string username, CreateBackupCommand command, Guid taskId)
    {
        backup.Failed(nameof(ErrorMessages.BackupOutcomeUnobserved), _clock.UtcNow);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        await _journal.RecordFailureAsync(
            AuditActions.BackupCreated, username, command.IpAddress, command.UserAgent, CancellationToken.None);
        await _tasks.FailAsync(taskId, nameof(ErrorMessages.BackupOutcomeUnobserved), CancellationToken.None);
    }
}
