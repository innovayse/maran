using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Queries.GetBackup;

/// <summary>Handles <see cref="GetBackupQuery"/> by reading one row within the caller's tenant scope.</summary>
/// <remarks>
/// Another account's backup answers not-found rather than forbidden, and nothing here decides that:
/// the context's global query filter hides the row, so the read simply finds nothing. A 403 would
/// confirm the row exists, which is the whole of what an enumeration of backup identifiers wants
/// (rules/security.md item 6).
/// </remarks>
public sealed class GetBackupQueryHandler
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
    public GetBackupQueryHandler(
        BackupsDbContext dbContext,
        BackupFailureDisplayNames failureNames,
        IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _failureNames = failureNames;
        _accounts = accounts;
    }

    /// <summary>Returns one backup with its owner's name, or a not-found failure.</summary>
    /// <param name="query">The backup to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The backup, or <c>BackupNotFound</c>.</returns>
    public async Task<Result<BackupDto>> HandleAsync(GetBackupQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var backup = await _dbContext.Backups
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.BackupId, cancellationToken);

        if (backup is null)
        {
            return Result<BackupDto>.Fail(Error.Of(nameof(ErrorMessages.BackupNotFound), ErrorType.NotFound));
        }

        var ownerNames = await _accounts.NamesOfAsync([backup.AccountId], cancellationToken);

        return Result<BackupDto>.Ok(BackupMapper.From(backup, ownerNames, _failureNames.Of(backup.FailureCode)));
    }
}
