using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Jobs;

/// <summary>
/// Removes, for every account, the trash entries older than the configured retention, and lets the agent
/// sweep the leftovers of copies that never finished.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this, nothing ever empties a trash.</b> A trashed entry keeps counting toward its account's
/// quota (it is renamed within the same filesystem), so a customer who deletes and never empties would
/// fill their quota with files they believe are gone. The agent keeps no schedule; this is the schedule.
/// </para>
/// <para>
/// <b>Every account is purged, a suspended one included.</b> Its trash occupies quota like any other.
/// The pass writes nothing but the purge and its journal entries.
/// </para>
/// <para>
/// <b>It runs with no principal</b>, so it reads the accounts through the unscoped window
/// <see cref="IAccountDirectory.ListAsync"/> — a sanctioned bypass already counted in rules/README.md,
/// and this handler adds none. A failed purge of one account is logged and journalled and the next
/// account proceeds: one account's broken home must not leave every other account's trash to grow.
/// The log carries counts and codes, never a path.
/// </para>
/// </remarks>
public sealed class TrashRetentionHandler
{
    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, Exception?> LogPass =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(TrashRetentionHandler)),
            "Trash retention removed {Removed} entries across {Accounts} accounts; {Failed} accounts could not be purged");

    /// <summary>Pre-compiled log delegate for an account the agent refused.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogRefused =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2, nameof(TrashRetentionHandler)),
            "Trash retention skipped account {AccountId}: the agent refused the purge with {Code}; "
            + "it will be tried again at the next pass");

    /// <summary>The unscoped window onto every account on the host.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the trash.</summary>
    private readonly IAgentFilesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>The retention, read at each pass so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<TrashRetentionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The unscoped window onto every account on the host.</param>
    /// <param name="agent">The agent client that purges.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="options">The module's limits.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public TrashRetentionHandler(
        IAccountDirectory accounts,
        IAgentFilesClient agent,
        FilesAuditJournal journal,
        IOptionsMonitor<FilesOptions> options,
        ILogger<TrashRetentionHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _options = options;
        _logger = logger;
    }

    /// <summary>Runs one pass over every account.</summary>
    /// <param name="message">The request to run a pass.</param>
    /// <param name="cancellationToken">Cancels the pass between accounts.</param>
    /// <returns>How many trash entries were removed across all accounts; zero is the ordinary outcome.</returns>
    public async Task<int> HandleAsync(TrashRetentionRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var retentionDays = (uint)_options.CurrentValue.TrashRetentionDays;
        var accounts = await _accounts.ListAsync(cancellationToken);

        var removed = 0;
        var failed = 0;
        foreach (var account in accounts)
        {
            var purged = await _agent.PurgeExpiredTrashAsync(account.Username, retentionDays, cancellationToken);
            var subject = FilesAuditJournal.SubjectOf(account.Username, string.Empty);

            if (!purged.IsSuccess)
            {
                failed++;
                LogRefused(_logger, account.Id, purged.Error!.Code, null);
                await _journal.RecordSystemAsync(
                    FileAuditActions.FileTrashRetentionPurged, subject, succeeded: false, CancellationToken.None);
                continue;
            }

            // Only a pass that removed something is journalled: one line per account per night saying
            // "nothing" would bury the lines that say a customer's files were destroyed.
            if (purged.Value > 0)
            {
                removed += (int)Math.Min(purged.Value, int.MaxValue);
                await _journal.RecordSystemAsync(
                    FileAuditActions.FileTrashRetentionPurged, subject, succeeded: true, CancellationToken.None);
            }
        }

        LogPass(_logger, removed, accounts.Count, failed, null);

        return removed;
    }
}
