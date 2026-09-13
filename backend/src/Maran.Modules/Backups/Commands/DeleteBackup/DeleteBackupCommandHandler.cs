using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Commands.DeleteBackup;

/// <summary>
/// Handles <see cref="DeleteBackupCommand"/>: removes the stored artifact through the agent and then
/// the row that named it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Artifact first, row second.</b> The two stores can disagree either way and this order decides
/// which. A row deleted first leaves an archive on the destination that nothing owns — a customer's
/// files and the contents of their database, sitting there for ever, invisible to the interface and
/// to retention alike. An artifact deleted first leaves, at worst, a row for an archive that is
/// already gone, which the next delete or the next listing resolves and which discloses nothing.
/// </para>
/// <para>
/// <b>An artifact that is already gone is a success, not a failure.</b> The agent answers not-found
/// for an artifact it cannot see, and this handler treats that as the deletion having happened: the
/// caller asked for the archive not to exist, and it does not. Refusing would leave the row
/// undeletable for ever, which is the state an operator can do nothing about.
/// </para>
/// <para>
/// <b>A running backup is refused.</b> Its artifact is being written by the agent at this moment,
/// and removing the file underneath a running archiver leaves a half-file whose row then says
/// completed. That refusal is the entity's own rule (<see cref="Domain.Entities.Backup.MayBeDeleted"/>),
/// not this handler's, so a second caller cannot get a different answer.
/// </para>
/// <para>
/// <b>A backup whose account has been deleted is still deletable, and that is the whole reason this
/// handler asks the ROW for the account's name before asking the directory.</b> A
/// <see cref="Domain.Enums.BackupKind.PreDeletion"/> row outlives its account by design, so the
/// directory answers "no such account" for it for ever — and while this handler read the name only
/// from the directory, every such row was permanently undeletable and its archive permanently
/// unreleasable. They accumulated, one per deleted account, with nothing in the product able to
/// remove either. The account cascade stamps the name onto the row at the last moment it exists
/// (<see cref="Domain.Entities.Backup.Orphan"/>), and this reads it from there.
/// </para>
/// <para>
/// <b>Another account's backup is not-found, never forbidden.</b> Nothing here checks the account:
/// the context's global query filter hides the row, so the lookup simply fails to find it (spec §8,
/// rules/security.md item 6).
/// </para>
/// </remarks>
public sealed class DeleteBackupCommandHandler
{
    /// <summary>
    /// The agent client's code for the wire's <c>ALREADY_EXISTS</c>, which on a deletion can only be
    /// the per-account lock refusing a second operation.
    /// </summary>
    /// <remarks>
    /// A literal rather than a <c>nameof</c>: the code is declared in the agent client's own resx
    /// and this module may not reference that project's resources, the same shape
    /// <c>CronAgentErrorTranslator</c> uses for the same reason.
    /// </remarks>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns every byte of every archive.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>Resolves the destination the artifact was written to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the artifact.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="destinations">Resolves the destination the artifact was written to.</param>
    public DeleteBackupCommandHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        IAgentBackupClient agent,
        BackupAuditJournal journal,
        BackupDestinationResolver destinations)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _destinations = destinations;
    }

    /// <summary>Deletes one backup's artifact and its row.</summary>
    /// <param name="command">The backup to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the artifact and the row are both gone — or <c>BackupNotFound</c>,
    /// <c>BackupStillRunning</c>, <c>AccountNotFound</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(
        DeleteBackupCommand command,
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

        if (!backup.MayBeDeleted())
        {
            // The account's name is not established yet on this path, so the trace records the
            // identifier the caller acted on.
            return await FailAsync(
                command,
                command.BackupId.ToString(),
                Error.Of(nameof(ErrorMessages.BackupStillRunning), ErrorType.Conflict),
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

        // The destination the artifact was written to, from the row. Deleting through the current
        // default would ask the agent to remove a file from somewhere the artifact never was, and
        // the agent's NotFound would then be read as "already gone".
        var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            return await FailAsync(command, username, destination.Error!, cancellationToken);
        }

        var deleted = await _agent.DeleteAsync(
            username, backup.Id.ToString(), destination.Value!.Agent, cancellationToken);
        if (!deleted.IsSuccess && !IsAlreadyGone(deleted.Error!))
        {
            return await FailAsync(command, username, Rename(deleted.Error!), cancellationToken);
        }

        _dbContext.Backups.Remove(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The account, not the backup id: the subject an operator will search for, resolved above
        // even for a deleted account's kept backup through the name the deletion stamped on it.
        await _journal.RecordSuccessAsync(
            AuditActions.BackupDeleted, username, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>The system user name whose directory this backup's archive sits in.</summary>
    /// <param name="backup">The backup being deleted.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The user name, or <c>null</c> when the account cannot be named at all.</returns>
    /// <remarks>
    /// <para>
    /// The row first, the directory second, and the order is what makes an orphaned backup
    /// releasable: the row carries a name only once its account has been deleted, so a stamped name
    /// is by construction the name of an account the directory can no longer answer for.
    /// </para>
    /// <para>
    /// The tenant question is already settled before this runs — the row was read through the
    /// context's global query filter, so a caller who reached it may see it — which is why reading
    /// the name from the row is not a bypass. What the directory lookup still buys for an ordinary
    /// backup is the current name of a live account, which is the only place that name may be read
    /// from while the account exists.
    /// </para>
    /// </remarks>
    private async Task<string?> ResolveAccountUsernameAsync(
        Domain.Entities.Backup backup,
        CancellationToken cancellationToken)
    {
        if (backup.NamesADeletedAccount())
        {
            return backup.OrphanedAccountUsername;
        }

        var account = await _accounts.FindAsync(backup.AccountId, cancellationToken);

        return account?.Username;
    }

    /// <summary>Whether the agent's refusal means the artifact was not there to begin with.</summary>
    /// <param name="error">The agent's typed failure.</param>
    /// <returns><c>true</c> when the artifact does not exist, which is the state the caller asked for.</returns>
    /// <remarks>
    /// Decided on the KIND rather than on the code's spelling. Reading a status out of a code's
    /// suffix is what the panel's error design forbids outright, and every reason it forbids it
    /// applies here: the codes are module-owned strings that can be renamed, while the kind is the
    /// thing the panel already derives an HTTP status from.
    /// </remarks>
    private static bool IsAlreadyGone(Error error)
    {
        return error.Type == ErrorType.NotFound;
    }

    /// <summary>Gives the agent's busy refusal this module's own words, and passes everything else through.</summary>
    /// <param name="error">The agent's typed failure.</param>
    /// <returns>
    /// <c>AccountBackupOperationRunning</c> where the agent refused because the account's lock was
    /// held, otherwise <paramref name="error"/> unchanged.
    /// </returns>
    /// <remarks>
    /// The agent's per-account lock answers a second operation with
    /// <c>BackupError::AlreadyRunning</c>, which it puts on the wire as <c>ALREADY_EXISTS</c>
    /// (<c>agent/crates/agent/src/services/backup/backup_status.rs</c>) — the same wire code as the
    /// idempotent outcome of a repeated CREATION, whose shared sentence reads "this already exists
    /// on your server, so nothing was created again". Told that, a customer whose deletion was
    /// merely queued behind a running backup would believe the archive was gone and never retry.
    ///
    /// The two are separable on THIS call, and only because of where they are produced: the agent
    /// raises <c>BackupError::AlreadyExists</c> in exactly one place, <c>create_backup.rs</c>, so a
    /// deletion can never receive it and the wire code can only be the lock. Unlike
    /// <see cref="IsAlreadyGone"/> this cannot be decided on the KIND — the idempotent creation and
    /// the busy lock are both conflicts — so the code is compared, and the const above says why the
    /// spelling is a literal.
    /// </remarks>
    private static Error Rename(Error error)
    {
        if (!string.Equals(error.Code, AgentAlreadyExistsCode, StringComparison.Ordinal))
        {
            return error;
        }

        return Error.Of(nameof(ErrorMessages.AccountBackupOperationRunning), ErrorType.Conflict);
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">
    /// What the refusal is recorded against: the account's system user name where the handler had
    /// resolved it, otherwise the identifier the caller acted on — recorded even for a backup the
    /// caller may not see, so a probe still leaves a trace naming what was probed for.
    /// </param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    /// <remarks>
    /// The one funnel every refusal passes through, so no early return can leave the journal
    /// unwritten.
    /// </remarks>
    private async Task<Result<bool>> FailAsync(
        DeleteBackupCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.BackupDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
