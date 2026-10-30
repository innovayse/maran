using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Queries.CountFailedBackups;

/// <summary>
/// Handles <see cref="CountFailedBackupsQuery"/> with one <c>count(*)</c> over <c>backups.Backups</c>.
/// </summary>
/// <remarks>
/// Scoped by the context's global query filter like every other read of the table, so a customer's
/// count is of their own backups and an administrator's is of every backup on the server.
/// </remarks>
public sealed class CountFailedBackupsQueryHandler
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    public CountFailedBackupsQueryHandler(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the failed backups in the caller's tenant scope.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountFailedBackupsQuery query, CancellationToken cancellationToken)
    {
        var count = await _dbContext.Backups
            .CountAsync(backup => backup.Status == BackupStatus.Failed, cancellationToken);

        return Result<int>.Ok(count);
    }
}
