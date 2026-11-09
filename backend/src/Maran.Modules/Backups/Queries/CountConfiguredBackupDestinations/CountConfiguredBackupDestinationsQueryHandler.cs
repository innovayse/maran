using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Queries.CountConfiguredBackupDestinations;

/// <summary>
/// Handles <see cref="CountConfiguredBackupDestinationsQuery"/> with two <c>count(*)</c> reads: the
/// destinations other than the seeded one, and the enabled host-wide schedules.
/// </summary>
/// <remarks>
/// The seeded destination is recognised by <see cref="BackupDestination.DefaultDestinationId"/>, the
/// fixed identity its seeder gives it, never by its name or its kind: an administrator may add a
/// second local destination, and that is a choice they made. The caller gates itself to
/// administrators, as the dashboard does.
/// </remarks>
public sealed class CountConfiguredBackupDestinationsQueryHandler
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    public CountConfiguredBackupDestinationsQueryHandler(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts what the administrator configured beyond the seeded destination.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(
        CountConfiguredBackupDestinationsQuery query,
        CancellationToken cancellationToken)
    {
        var seeded = BackupDestination.DefaultDestinationId;

        var destinations = await _dbContext.BackupDestinations
            .CountAsync(destination => destination.Id != seeded, cancellationToken);
        // The host-wide schedule is visible to an administrator only (the module's tenant filter), and
        // the caller gates itself to administrators, so a non-administrator would count zero here.
        var schedules = await _dbContext.BackupSchedules
            .CountAsync(schedule => schedule.AccountId == null && schedule.EnabledAt != null, cancellationToken);

        return Result<int>.Ok(destinations + schedules);
    }
}
