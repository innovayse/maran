using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// Handles <see cref="ListBackupsQuery"/> by reading one page of <c>backups.Backups</c> within the
/// caller's tenant scope.
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
/// The tenant boundary is not any <c>Where</c> clause here: the context's global query filter
/// supplies it, before the count as well as before the page, so a customer's total can never count
/// another tenant's rows (spec §8). Every clause below only narrows that scope.
/// </para>
/// <para>
/// <b>The owner's name belongs to the Accounts module, and this handler never reads its tables.</b>
/// A search on it is answered in two steps through <see cref="IAccountDirectory"/>: the directory
/// returns the ids of every account whose name matches — tenant-scoped and uncapped — and the rows
/// are then filtered by those ids here, ORed with the username a deleted account left on its rows.
/// The names printed beside the rows come from the same directory, in one batched lookup per page.
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

    /// <summary>
    /// Filters by account, status, kind and start window, searches the owner's name, and answers the
    /// requested page with the total that matched and each row's owner name.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<BackupDto>>> HandleAsync(
        ListBackupsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Backup> backups = _dbContext.Backups.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            backups = backups.Where(backup => backup.AccountId == accountId);
        }

        if (query.Status is { } status)
        {
            backups = backups.Where(backup => backup.Status == status);
        }

        if (query.Kind is { } kind)
        {
            backups = backups.Where(backup => backup.Kind == kind);
        }

        if (query.From is { } from)
        {
            backups = backups.Where(backup => backup.StartedAt >= from);
        }

        if (query.To is { } to)
        {
            backups = backups.Where(backup => backup.StartedAt <= to);
        }

        if (!query.Search.IsNone)
        {
            // Captured locals, so EF Core sends them as parameters; LikePattern escapes the
            // wildcards so the term matches literally.
            var term = query.Search.Value;
            var pattern = LikePattern.Contains(term);
            var owners = await _accounts.FindIdsByNameAsync(term, cancellationToken);
            backups = backups.Where(backup =>
                owners.Contains(backup.AccountId)
                || EF.Functions.ILike(backup.OrphanedAccountUsername, pattern, LikePattern.EscapeCharacter));
        }

        var page = await backups.ToPagedAsync(query, ListBackupsSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row. A
        // deleted account is absent from it, and its rows say whose they were through
        // OrphanedAccountUsername instead.
        var ownerIds = page.Items
            .Select(backup =>
            {
                return backup.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        // The failure NAME is resolved here, in memory, from this request's culture through a resx
        // table — which is why the page is read whole and mapped after the query rather than
        // projected inside it: no expression tree can translate the resolver into SQL.
        return Result<PagedResult<BackupDto>>.Ok(page.Map(backup =>
        {
            return BackupMapper.From(backup, ownerNames, _failureNames.Of(backup.FailureCode));
        }));
    }
}
