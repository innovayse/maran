using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Interfaces;

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
/// The tenant boundary is not the <c>Where</c> clause on the account here: the context's global
/// query filter supplies it, so this handler could not leak another tenant's rows even if it were
/// rewritten carelessly (spec §8). The clause only narrows that scope to the account the caller
/// asked for, and is ANDed with the filter, so a customer naming another account gets nothing.
/// </para>
/// </remarks>
public sealed class ListBackupsQueryHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Names a recorded failure code in the caller's language.</summary>
    private readonly BackupFailureDisplayNames _failureNames;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and its collaborators.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="failureNames">Names a recorded failure code in the caller's language.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    public ListBackupsQueryHandler(
        BackupsDbContext dbContext,
        BackupFailureDisplayNames failureNames,
        IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
        _accounts = accounts;
    }

    /// <summary>Returns the backups the caller may see, newest first, each with its owner's name.</summary>
    /// <param name="query">The list request; its optional account narrows the tenant scope.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the backups; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<BackupDto>>> HandleAsync(
        ListBackupsQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Backups
            .AsNoTracking()
            .Where(backup => query.AccountId == null || backup.AccountId == query.AccountId)
            .OrderByDescending(backup => backup.StartedAt)
            .ToListAsync(cancellationToken);

        // One batched, tenant-scoped lookup for every owner in the answer, never one per row. A
        // deleted account is absent from it, and its rows say whose they were through
        // OrphanedAccountUsername instead.
        var ownerIds = rows
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        // The failure NAME is resolved here, in memory, from this request's culture through a resx
        // table — which is also why the rows are read whole and mapped after the query rather than
        // projected inside it: no expression tree can translate the resolver into SQL.
        IReadOnlyList<BackupDto> backups = rows
            .Select(row =>
            {
                return BackupMapper.From(row, ownerNames, _failureNames.Of(row.FailureCode));
            })
            .ToList();

        return Result<IReadOnlyList<BackupDto>>.Ok(backups);
    }
}
