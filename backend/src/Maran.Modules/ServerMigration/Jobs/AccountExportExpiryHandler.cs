using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>
/// Removes every export whose retention has ended: the backup artifact in the agent's root-only directory, then the
/// record, which takes its grants with it (issue #127, decision D11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this a package outlives its promise.</b> <c>AccountExport.ExpiresAt</c> only stops the panel serving
/// the package; the artifact, a complete copy of a customer's files and databases in plaintext, stays on disk until
/// something removes it, and nothing else does (a migration backup is in no retention allow-list). The seven days
/// the screen promises are therefore enforced here.
/// </para>
/// <para>
/// <b>Artifact first, record second</b>, the order the backup deletion uses and for the same reason: a record
/// deleted first leaves an archive nothing owns and nothing can find. A removal that fails leaves the record, so the
/// next pass tries again, and the failure is logged with the account and never with a path. Each export is handled
/// on its own, so one that cannot be removed does not hold up the rest. A panel composed without Backups has
/// nothing to remove and the pass says so in its log instead of deleting records whose artifacts it never touched.
/// </para>
/// <para>
/// <b>No tenant scope is bypassed here.</b> This module's rows are administrator bookkeeping with no query filter;
/// the one bypass the work needs is inside the Backups module's <c>DiscardExportAsync</c>, which removes only a
/// backup of the export kind. Each removal is journalled under the panel's own name, not a person's.
/// </para>
/// </remarks>
public sealed class AccountExportExpiryHandler
{
    /// <summary>How many exports one pass removes, so a first pass after a long outage is many short steps.</summary>
    private const int BatchSize = 100;

    /// <summary>Pre-compiled log delegate for a finished pass.</summary>
    private static readonly Action<ILogger, int, int, Exception?> LogPass =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, nameof(AccountExportExpiryHandler)),
            "Removed {Removed} expired account exports; {Kept} could not be removed and will be tried again");

    /// <summary>Pre-compiled log delegate for a pass on a panel that has no Backups module.</summary>
    private static readonly Action<ILogger, Exception?> LogNoBackups =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(AccountExportExpiryHandler)),
            "Expired account exports were not removed: this panel has no Backups module to remove their artifacts");

    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Removes an export's backup; absent when the panel has no Backups module.</summary>
    private readonly IMigrationArtifactService? _artifacts;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records each removal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Operator log.</summary>
    private readonly ILogger<AccountExportExpiryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="artifacts">Removes an export's backup; empty when the panel has no Backups module.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records each removal.</param>
    /// <param name="logger">Operator log.</param>
    public AccountExportExpiryHandler(
        ServerMigrationDbContext dbContext,
        IEnumerable<IMigrationArtifactService> artifacts,
        IClock clock,
        ServerMigrationAuditJournal journal,
        ILogger<AccountExportExpiryHandler> logger)
    {
        _dbContext = dbContext;
        _artifacts = artifacts.FirstOrDefault();
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Runs one pass.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between exports.</param>
    /// <returns>How many exports were removed; zero is the ordinary outcome.</returns>
    public async Task<int> HandleAsync(AccountExportExpiryRequested message, CancellationToken cancellationToken)
    {
        if (_artifacts is null)
        {
            LogNoBackups(_logger, null);

            return 0;
        }

        var now = _clock.UtcNow;
        var expired = await _dbContext.AccountExports
            .Where(export => export.ExpiresAt <= now)
            .OrderBy(export => export.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var removed = 0;

        foreach (var export in expired)
        {
            var discarded = await _artifacts.DiscardExportAsync(export.BackupId, export.SourceAccountName, cancellationToken);

            await _journal.RecordSystemAsync(
                MigrationAuditActions.ExportExpired, export.SourceAccountName, discarded.IsSuccess, cancellationToken);

            if (!discarded.IsSuccess)
            {
                continue;
            }

            _dbContext.AccountExports.Remove(export);
            await _dbContext.SaveChangesAsync(cancellationToken);
            removed++;
        }

        LogPass(_logger, removed, expired.Count - removed, null);

        return removed;
    }
}
