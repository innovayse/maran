using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Queries.GetBackupSchedule;

/// <summary>
/// Handles <see cref="GetBackupScheduleQuery"/> by reading <c>backups.BackupSchedules</c> within the
/// caller's tenant scope.
/// </summary>
/// <remarks>
/// <b>A scope that has never been configured is answered as such, and the read does not fail.</b>
/// "Nothing is scheduled here" is the true state of a fresh server, so it is a successful
/// <see cref="BackupScheduleScopeDto"/> with no schedule in it — never an invented default, which an
/// operator would trust, and never a not-found, which would report the ordinary first state of the
/// screen as a failed request (issue #211).
///
/// There is no <c>Where</c> clause on the account beyond the one the query names: the context's
/// global query filter supplies the tenant predicate, so this handler could not read another
/// tenant's schedule even if it were rewritten carelessly (spec §8). A row the filter hides reads as
/// "nothing recorded", exactly like a row that does not exist.
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

    /// <summary>Returns what is recorded for the named scope.</summary>
    /// <param name="query">Which schedule to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The scope's schedule, or a scope with no schedule when none has been saved.</returns>
    public async Task<BackupScheduleScopeDto> HandleAsync(
        GetBackupScheduleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var schedule = await _dbContext.BackupSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.AccountId == query.AccountId, cancellationToken);

        return new BackupScheduleScopeDto(schedule is null ? null : BackupScheduleMapper.From(schedule));
    }
}
