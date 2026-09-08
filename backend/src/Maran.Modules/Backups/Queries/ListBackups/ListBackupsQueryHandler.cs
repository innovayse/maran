using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;

namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// Handles <see cref="ListBackupsQuery"/> by reading <c>backups.Backups</c> within the caller's
/// tenant scope, newest first.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent's own listing is deliberately not called here, and no listing in this module may
/// call it.</b> The agent reports what is on the destination — every artifact in the account's
/// directory, including one a half-finished run left behind and one whose row has been deleted. The
/// panel's rows are the record of which backups the panel took and how they ended, and they are the
/// only answer a screen may be built from. The agent's listing has one legitimate consumer, and it
/// is retention, which is exactly the caller that needs to see an artifact no row owns.
/// </para>
/// <para>
/// There is no <c>Where</c> clause on the account here, and deliberately not one: the context's
/// global query filter supplies it, so this handler could not leak another tenant's rows even if it
/// were rewritten carelessly (spec §8).
/// </para>
/// </remarks>
public sealed class ListBackupsQueryHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Names a recorded failure code in the caller's language.</summary>
    private readonly BackupFailureDisplayNames _failureNames;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="failureNames">Names a recorded failure code in the caller's language.</param>
    public ListBackupsQueryHandler(BackupsDbContext dbContext, BackupFailureDisplayNames failureNames)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
    }

    /// <summary>Returns the backups the caller may see, newest first.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the backups; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<BackupDto>>> HandleAsync(
        ListBackupsQuery query,
        CancellationToken cancellationToken)
    {
        // The projection stops at the row's own columns and the failure NAME is added afterwards,
        // in memory. It has to be: the name is resolved from this request's culture through a resx
        // table, which no expression tree can be translated into SQL — a Select that called the
        // resolver would either fail to translate or, worse, silently pull the whole table client
        // side without saying so.
        var rows = await _dbContext.Backups
            .AsNoTracking()
            .OrderByDescending(backup => backup.StartedAt)
            .Select(backup => new
            {
                backup.Id,
                backup.AccountId,
                backup.Status,
                backup.Kind,
                backup.SizeBytes,
                backup.Sha256,
                backup.DatabaseCount,
                backup.StartedAt,
                backup.FinishedAt,
                backup.FailureCode,
            })
            .ToListAsync(cancellationToken);

        var backups = rows
            .Select(row =>
            {
                return new BackupDto(
                    row.Id,
                    row.AccountId,
                    row.Status,
                    row.Kind,
                    row.SizeBytes,
                    row.Sha256,
                    row.DatabaseCount,
                    row.StartedAt,
                    row.FinishedAt,
                    row.FailureCode,
                    _failureNames.Of(row.FailureCode));
            })
            .ToList();

        return Result<IReadOnlyList<BackupDto>>.Ok(backups);
    }
}
