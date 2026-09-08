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
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.BackupNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (!backup.MayBeDeleted())
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.BackupStillRunning), ErrorType.Conflict), cancellationToken);
        }

        var username = await ResolveAccountUsernameAsync(backup, cancellationToken);
        if (username is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // The destination the artifact was written to, from the row. Deleting through the current
        // default would ask the agent to remove a file from somewhere the artifact never was, and
        // the agent's NotFound would then be read as "already gone".
        var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            return await FailAsync(command, destination.Error!, cancellationToken);
        }

        var deleted = await _agent.DeleteAsync(
            username, backup.Id.ToString(), destination.Value!.Agent, cancellationToken);
        if (!deleted.IsSuccess && !IsAlreadyGone(deleted.Error!))
        {
            return await FailAsync(command, deleted.Error!, cancellationToken);
        }

        _dbContext.Backups.Remove(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.BackupDeleted, command.BackupId, command.IpAddress, command.UserAgent, cancellationToken);

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

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused, whose backup id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    /// <remarks>
    /// The one funnel every refusal passes through, so no early return can leave the journal
    /// unwritten — including the refusal for a backup the caller may not see, which is recorded
    /// under the id that was probed for.
    /// </remarks>
    private async Task<Result<bool>> FailAsync(
        DeleteBackupCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.BackupDeleted, command.BackupId, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
