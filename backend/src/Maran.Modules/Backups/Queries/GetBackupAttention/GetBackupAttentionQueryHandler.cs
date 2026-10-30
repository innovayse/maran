using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Queries.GetBackupAttention;

/// <summary>
/// Handles <see cref="GetBackupAttentionQuery"/>: counts the failed backups no later finished run of
/// the same account supersedes, and names the newest.
/// </summary>
/// <remarks>
/// A failed row is unrecovered when its account has no finished — completed or failed — row that
/// started after it, which is "the account's latest finished run failed" stated per row, so it
/// translates into one <c>NOT EXISTS</c>. The tenant boundary is the context's global query filter,
/// which applies inside the subquery as well, so a customer's answer is about their own account.
/// </remarks>
public sealed class GetBackupAttentionQueryHandler
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
    public GetBackupAttentionQueryHandler(
        BackupsDbContext dbContext,
        BackupFailureDisplayNames failureNames,
        IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
        _accounts = accounts;
    }

    /// <summary>Counts the unrecovered failures in the caller's scope and reads the newest.</summary>
    /// <param name="query">The request; its optional account narrows the tenant scope.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the count and the newest failure; this never fails.</returns>
    public async Task<Result<BackupAttentionDto>> HandleAsync(
        GetBackupAttentionQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Backup> backups = _dbContext.Backups.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            backups = backups.Where(backup => backup.AccountId == accountId);
        }

        var unrecovered = backups.Where(failed =>
            failed.Status == BackupStatus.Failed
            && !_dbContext.Backups.Any(later =>
                later.AccountId == failed.AccountId
                && later.Status != BackupStatus.Running
                && later.StartedAt > failed.StartedAt));

        // One row per account: two failures of one account sharing a start instant are one account.
        var count = await unrecovered
            .Select(failed => failed.AccountId)
            .Distinct()
            .CountAsync(cancellationToken);

        var newest = await unrecovered
            .OrderByDescending(failed => failed.StartedAt)
            .ThenBy(failed => failed.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is null)
        {
            return Result<BackupAttentionDto>.Ok(new BackupAttentionDto(0, null));
        }

        var ownerNames = await _accounts.NamesOfAsync([newest.AccountId], cancellationToken);

        return Result<BackupAttentionDto>.Ok(new BackupAttentionDto(
            count,
            BackupMapper.From(newest, ownerNames, _failureNames.Of(newest.FailureCode))));
    }
}
