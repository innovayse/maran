using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.RestoreBackup;

/// <summary>
/// Handles <see cref="RestoreBackupCommand"/>: replaces one account from one of its backups, and
/// reports what was actually replaced (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written forward from the question: if this fails halfway, what state is the account in?</b>
/// The agent answers that for the machine — it parks the home before swapping it, dumps every
/// database before dropping it, and reloads those dumps in reverse order if a load fails. What THIS
/// handler answers is the panel's half: whether the customer is told the truth about it. Every
/// refusal below happens before the agent is asked for anything, so a refused restore has changed
/// nothing at all; once the stream starts, the only thing the panel decides is how to RECORD what
/// came back, and it decides it once.
/// </para>
/// <para>
/// <b>The point of no return is the agent's first <c>DROP DATABASE</c>, and it is on the other side
/// of every check here.</b> The order is deliberate: read the row, prove the artifact is usable,
/// resolve the account, match the confirmation, refuse a concurrent run, and only then open a task
/// and stream. Each of those is free to fail.
/// </para>
/// <para>
/// <b>Concurrency is refused in two places and only one of them is here — say which.</b> The panel
/// can observe a CREATE that is in flight, because a create writes a <see cref="BackupStatus.Running"/>
/// row before it calls the agent, and that is the refusal a customer sees, with this module's own
/// code. The panel cannot observe a RESTORE in flight: a restore writes no row of its own, so there
/// is nothing here to look at, and a check pretending otherwise would be a check that cannot observe
/// what it reports on (rules/testing.md). That half is held by the agent's per-account lock, which
/// is taken without waiting and answers <c>AlreadyRunning</c>, and the customer sees the agent's
/// code rather than this module's. The difference is visible in the error a customer gets, so it is
/// written down rather than smoothed over.
/// </para>
/// <para>
/// <b>The verdict is stated once, in <see cref="RestoreRunner"/>, and read here.</b> A restore that
/// replaced the home and eleven of twelve databases is a FAILURE — the account is now a home from
/// one moment over a database from another — and this handler answers a failed
/// <see cref="Result{T}"/> for it rather than an outcome the caller has to inspect. That is the
/// opposite of the create path's deliberate choice to answer success carrying a failed row, and the
/// reason differs: a create that failed left the account untouched and a screen simply lists it,
/// whereas a partial restore has already changed the account and the caller must not be able to
/// treat the answer as routine.
/// </para>
/// <para>
/// <b>What a restore does NOT restore.</b> The vhosts, the certificates, the crontab, the firewall
/// rules and the SFTP logins are outside the archive and are untouched. This is replace-within-scope,
/// not undo, and the doc says so here because a reader who believes otherwise will not look again.
/// </para>
/// </remarks>
public sealed class RestoreBackupCommandHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The one window onto which databases the panel knows this account owns.</summary>
    private readonly IAccountDatabaseDirectory _databases;

    /// <summary>The stream consumer that reduces one restore to a single outcome.</summary>
    private readonly RestoreRunner _runner;

    /// <summary>Resolves the destination the artifact was written to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The panel-wide task journal, so an operator can watch a restore instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The current request's correlation id, recorded on the task beside its stages.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="databases">The databases the panel knows the account owns.</param>
    /// <param name="destinations">Resolves the destination the artifact was written to.</param>
    /// <param name="runner">The stream consumer that drives the restore.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    public RestoreBackupCommandHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        IAccountDatabaseDirectory databases,
        RestoreRunner runner,
        BackupDestinationResolver destinations,
        BackupAuditJournal journal,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _databases = databases;
        _runner = runner;
        _destinations = destinations;
        _journal = journal;
        _tasks = tasks;
        _correlationIds = correlationIds;
    }

    /// <summary>Restores one account from one of its backups.</summary>
    /// <param name="command">The validated request; see <see cref="RestoreBackupCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// What the restore did, when it did all of it — or a typed failure. A PARTIAL restore is a
    /// failure carrying the outcome's code, because the account has been changed and no caller may
    /// read it as routine.
    /// </returns>
    public async Task<Result<RestoreOutcomeDto>> HandleAsync(
        RestoreBackupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Tenant-scoped by the context's global filter: another customer's backup is simply not
        // found, so a guessed id reads as 404 rather than confirming the row exists.
        var backup = await _dbContext.Backups
            .FirstOrDefaultAsync(row => row.Id == command.BackupId, cancellationToken);

        if (backup is null)
        {
            return await RefuseAsync(
                command, Guid.Empty, nameof(ErrorMessages.BackupNotFound), ErrorType.NotFound, cancellationToken);
        }

        // Only a completed backup has an artifact and a digest. A Running one is being written at
        // this moment and a Failed one produced nothing, and the agent refuses an empty expected
        // digest — so this refusal is here rather than left to surface from the agent halfway
        // through the operation that must not be surprising.
        if (backup.Status != BackupStatus.Completed)
        {
            return await RefuseAsync(
                command, backup.Id, nameof(ErrorMessages.BackupNotRestorable), ErrorType.Conflict, cancellationToken);
        }

        var account = await _accounts.FindAsync(backup.AccountId, cancellationToken);
        if (account is null)
        {
            // The row was visible and the account is not. Today this is the final backup of a
            // deleted account, which is deliberately kept and deliberately not restorable: there is
            // no system user to restore into, and re-creating one from a backup is a different
            // operation nobody has asked for.
            return await RefuseAsync(
                command, backup.Id, nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound, cancellationToken);
        }

        // Ordinal, case-sensitive, and against the TARGET account rather than the caller's own: an
        // administrator restoring on a customer's behalf types the customer's name. Linux user names
        // are case-sensitive, so a case-insensitive comparison here would accept a confirmation that
        // is not the account's name.
        if (!string.Equals(command.ConfirmAccountUsername, account.Username, StringComparison.Ordinal))
        {
            return await RefuseAsync(
                command, backup.Id, nameof(ErrorMessages.RestoreConfirmationMismatch), ErrorType.Conflict, cancellationToken);
        }

        if (await HasRunningBackupAsync(backup.AccountId, cancellationToken))
        {
            return await RefuseAsync(
                command, backup.Id, nameof(ErrorMessages.BackupStillRunning), ErrorType.Conflict, cancellationToken);
        }

        // The panel's own rows, never the server's listing: a database the server has and the panel
        // has forgotten must not be re-created by a restore, because what would come back is a
        // database with a user nothing points at.
        var allowedDatabases = await _databases.ListNamesAsync(backup.AccountId, cancellationToken);

        // The destination the backup was WRITTEN to, read from the row, never this server's current
        // default. A restore that resolved the default would read the wrong place the moment a
        // second destination existed, and it would do it silently.
        var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            return await RefuseAsync(
                command, backup.Id, destination.Error!.Code, destination.Error.Type, cancellationToken);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.BackupRestore, account.Username, _correlationIds.CorrelationId, cancellationToken);

        var outcome = await _runner.RunAsync(
            account.Username,
            backup.Id,
            backup.Sha256,
            allowedDatabases,
            destination.Value!.Agent,
            taskId,
            cancellationToken);

        var dto = new RestoreOutcomeDto(
            backup.Id,
            backup.AccountId,
            outcome.Whole,
            outcome.FilesRestored,
            outcome.DatabasesRestored,
            outcome.DatabasesTotal,
            outcome.Failure?.Code ?? string.Empty);

        // Task, journal and answer all written from `outcome.Whole` — the one verdict the runner
        // stated — never from three readings of the counts.
        if (!outcome.Whole)
        {
            // The runner's own error, carried whole. Re-deciding the KIND here is what answered
            // HTTP 500 over an artifact the agent had refused as unusable: `AgentValidationFailed`
            // is an ErrorType.Validation the agent client had already decided, and rebuilding the
            // error from its code alone discarded it (rules/csharp.md "Every failure states its
            // KIND, and the kind is what an HTTP status is derived from").
            var failure = outcome.Failure!;

            await _journal.RecordFailureAsync(
                AuditActions.BackupRestored, backup.Id, command.IpAddress, command.UserAgent, cancellationToken);
            await _tasks.FailAsync(taskId, failure.Code, cancellationToken);

            return Result<RestoreOutcomeDto>.Fail(failure);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.BackupRestored, backup.Id, command.IpAddress, command.UserAgent, cancellationToken);
        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<RestoreOutcomeDto>.Ok(dto);
    }

    /// <summary>Whether a backup of this account is being written right now.</summary>
    /// <param name="accountId">The account the restore would replace.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when a create is in flight for the account.</returns>
    /// <remarks>
    /// Restoring over an account whose archive is half-written would have the agent reading a home
    /// that <c>tar</c> is walking. The agent's lock refuses it too; this exists so the CUSTOMER is
    /// told, in this module's own words, rather than being handed a code from the root daemon about
    /// a lock they have no way to reason about.
    /// </remarks>
    private async Task<bool> HasRunningBackupAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _dbContext.Backups
            .AnyAsync(
                row => row.AccountId == accountId && row.Status == BackupStatus.Running,
                cancellationToken);
    }

    /// <summary>Journals a restore that was refused before the agent was asked, and returns it.</summary>
    /// <param name="command">The restore that was refused.</param>
    /// <param name="backupId">
    /// The backup refused, or <see cref="Guid.Empty"/> when no row was found. Recorded even then, so
    /// a probe for another tenant's backup still leaves a trace saying one was attempted.
    /// </param>
    /// <param name="code">The machine-stable code, which is also its resx key.</param>
    /// <param name="type">The kind of failure, from which the HTTP status is derived.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying the error.</returns>
    /// <remarks>
    /// No panel task is closed here because none was opened: every refusal this funnels is upstream
    /// of <c>BeginAsync</c>. A task for an operation that never started would be a row an operator
    /// can only read as a restore that vanished.
    /// </remarks>
    private async Task<Result<RestoreOutcomeDto>> RefuseAsync(
        RestoreBackupCommand command,
        Guid backupId,
        string code,
        ErrorType type,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.BackupRestored, backupId, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<RestoreOutcomeDto>.Fail(Error.Of(code, type));
    }
}
