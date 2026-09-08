using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Backups.Jobs;

/// <summary>
/// Deletes an account's oldest successful backups beyond the number its schedule keeps — the archive
/// and the row together, never one without the other (R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this, nothing in the panel ever bounds a backup.</b> Every scheduled run writes an
/// archive of a whole account, and until this handler existed no code path removed one; seven
/// dailies of a twenty-gigabyte account is a hundred and forty gigabytes, and the eighth night is
/// the same again.
/// </para>
/// <para>
/// <b>The archive is deleted BEFORE the row, which is the same order the customer's own deletion
/// uses and for the same reason.</b> A row removed first leaves an archive that nothing owns — a
/// customer's files and the contents of their database, sitting on the disk for ever, invisible to
/// the interface and to this handler alike. An archive removed first leaves at worst a row for
/// bytes that are already gone, which the next listing resolves and which discloses nothing.
/// </para>
/// <para>
/// <b>What it does about a destination it cannot act on: nothing, loudly.</b> The only destination
/// this panel can address is the operator's configured local root — the agent refuses a remote
/// destination at its own boundary and there is no destination table yet — so a row naming any
/// other destination is skipped and neither its archive nor its row is touched. That is deliberate
/// and it is the honest half of the trade: a retention pass that removed a row whose bytes it could
/// not reach would report a tidy table over an archive of a customer's database that nobody can now
/// find, and a pass that deleted the FIRST reachable thing it saw would be the same defect one step
/// later. Such a row is left in place, counted, and reported.
/// </para>
/// <para>
/// <b>A refusal from the agent stops the pass for that account rather than moving on.</b> Three
/// reasons in the order they bind: the agent's per-account lock means the likeliest refusal is
/// <c>AlreadyRunning</c>, and the operation holding that lock is the restore this pass must not
/// delete under (R12's "never the backup a restore is reading" — enforced there by construction,
/// not by a marker on the row this panel does not have); a destination that has become unreachable
/// will refuse every subsequent row identically, so continuing is a burst of failing calls against
/// a root process; and pruning out of order would remove a newer archive while an older one stayed,
/// which is not what "keep the most recent seven" means.
/// </para>
/// <para>
/// <b>Only completed ordinary backups are counted and pruned</b>
/// (<see cref="Backup.MayBeRetentionPruned"/>). A pre-restore or pre-deletion copy is a safety copy
/// and a safety copy retention can eat is not a safety copy; a failed row pins nothing worth
/// keeping and, if it counted, a bad week would silently expire every good copy the account had.
/// </para>
/// </remarks>
public sealed class RetentionHandler
{
    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, Guid, int, Exception?> LogPruned =
        LoggerMessage.Define<int, Guid, int>(
            LogLevel.Information,
            new EventId(1, nameof(RetentionHandler)),
            "Retention removed {Pruned} backups of account {AccountId}; {Unreachable} were left because "
            + "their destination cannot be addressed by this panel");

    /// <summary>Pre-compiled log delegate for a pass the agent refused.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogRefused =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2, nameof(RetentionHandler)),
            "Retention stopped at backup {BackupId}: the agent refused the deletion with {Code}. "
            + "Nothing was removed for it and the pass will be retried after the next scheduled run");

    /// <summary>Pre-compiled log delegate for an account the pass could not name.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountMissing =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(3, nameof(RetentionHandler)),
            "Retention skipped account {AccountId}: it no longer exists, so its archives cannot be addressed");

    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The unscoped window onto every account on the host.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns every byte of every archive.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>Resolves the destination each pruned artifact was written to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<RetentionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The unscoped window onto every account on the host.</param>
    /// <param name="agent">The agent client that removes an artifact.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="destinations">Resolves the destination each pruned artifact was written to.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public RetentionHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        IAgentBackupClient agent,
        BackupAuditJournal journal,
        BackupDestinationResolver destinations,
        ILogger<RetentionHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _destinations = destinations;
        _logger = logger;
    }

    /// <summary>Runs one retention pass over one account's backups.</summary>
    /// <param name="message">Which account to prune, and how many backups to keep.</param>
    /// <param name="cancellationToken">Cancels the pass between deletions.</param>
    /// <returns>How many backups were removed; zero is the ordinary outcome until the count is exceeded.</returns>
    public async Task<int> HandleAsync(RetentionRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var accounts = await _accounts.ListAsync(cancellationToken);
        var account = accounts.FirstOrDefault(row => { return row.Id == message.AccountId; });
        if (account is null)
        {
            LogAccountMissing(_logger, message.AccountId, null);
            return 0;
        }

        var prunable = (await OwnedBackupsAsync(message.AccountId, cancellationToken))
            .Where(backup => { return backup.MayBeRetentionPruned(); })
            .OrderByDescending(backup => { return backup.StartedAt; })
            .Skip(message.RetainCount)
            .ToList();

        var pruned = 0;
        var unreachable = 0;

        // Oldest first, so a pass interrupted partway — a cancellation, a host shutting down — has
        // already removed the longest-overdue archives rather than an arbitrary subset.
        foreach (var backup in Enumerable.Reverse(prunable))
        {
            // Resolved per backup, from the row, because two backups of one account can name two
            // destinations. A destination this build cannot act on leaves the row alone and is
            // COUNTED rather than skipped silently — the count is what tells an operator that
            // retention is not keeping the promise the schedule's retain figure makes.
            var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);
            if (!destination.IsSuccess)
            {
                unreachable++;
                continue;
            }

            var deleted = await _agent.DeleteAsync(
                account.Username, backup.Id.ToString(), destination.Value!.Agent, cancellationToken);

            // A NotFound is the state the pass wanted: the bytes are gone, so the row may go too.
            // Decided on the error's KIND rather than on the code's spelling, as the customer's own
            // deletion path decides it — a code is a module-owned string that can be renamed.
            if (!deleted.IsSuccess && deleted.Error!.Type != ErrorType.NotFound)
            {
                LogRefused(_logger, backup.Id, deleted.Error!.Code, null);

                await _journal.RecordScheduledAsync(
                    AuditActions.BackupRetentionPruned, backup.Id, succeeded: false, cancellationToken);

                break;
            }

            _dbContext.Backups.Remove(backup);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _journal.RecordScheduledAsync(
                AuditActions.BackupRetentionPruned, backup.Id, succeeded: true, cancellationToken);

            pruned++;
        }

        LogPruned(_logger, pruned, message.AccountId, unreachable, null);

        return pruned;
    }

    /// <summary>Reads every backup of one account, ignoring the tenant filter this pass cannot satisfy.</summary>
    /// <param name="accountId">The account being pruned.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account's backup rows, tracked so the pruned ones can be removed.</returns>
    /// <remarks>
    /// One account's backups, so materialising them is affordable; and materialising is what lets
    /// <see cref="Backup.MayBeRetentionPruned"/> be the rule rather than a translation of it. A
    /// <c>Kind != PreDeletion</c> in the SQL would be a second spelling of a rule that decides
    /// whether a customer's last copy is destroyed, and the two could drift apart without either
    /// side failing to compile — the same argument the account cascade makes about its own
    /// predicate.
    /// </remarks>
    private Task<List<Backup>> OwnedBackupsAsync(Guid accountId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended retention with no principal; scoped, it would prune nothing, ever
        return _dbContext.Backups
            .IgnoreQueryFilters()
            .Where(backup => backup.AccountId == accountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }
}
