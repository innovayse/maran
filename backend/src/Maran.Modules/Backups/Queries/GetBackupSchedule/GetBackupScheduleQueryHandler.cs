using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Queries.GetBackupSchedule;

/// <summary>
/// Handles <see cref="GetBackupScheduleQuery"/> by reading <c>backups.BackupSchedules</c> within the
/// caller's tenant scope.
/// </summary>
/// <remarks>
/// <b>A schedule that has never been configured is not-found rather than an invented default.</b>
/// Answering with a made-up "daily at 03:00, disabled" would show an operator a schedule the server
/// does not hold, and the first thing they would do is trust it. Not-found says the true thing: this
/// server has no backup schedule, and saving the form is what creates one.
///
/// There is no <c>Where</c> clause on the account beyond the one the query names: the context's
/// global query filter supplies the tenant predicate, so this handler could not read another
/// tenant's schedule even if it were rewritten carelessly (spec §8).
/// </remarks>
public sealed class GetBackupScheduleQueryHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    public GetBackupScheduleQueryHandler(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the named schedule, or not-found when there is none.</summary>
    /// <param name="query">Which schedule to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The schedule, or <c>BackupScheduleNotFound</c>.</returns>
    public async Task<Result<BackupScheduleDto>> HandleAsync(
        GetBackupScheduleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var schedule = await _dbContext.BackupSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.AccountId == query.AccountId, cancellationToken);

        if (schedule is null)
        {
            return Result<BackupScheduleDto>.Fail(
                Error.Of(nameof(ErrorMessages.BackupScheduleNotFound), ErrorType.NotFound));
        }

        return Result<BackupScheduleDto>.Ok(BackupScheduleMapper.From(schedule));
    }
}
