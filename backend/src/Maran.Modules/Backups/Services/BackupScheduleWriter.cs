using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// The one place a backup schedule row is created or reconfigured, for the administrator's surface
/// and the customer's alike.
/// </summary>
/// <remarks>
/// <para>
/// <b>The read before the insert is what keeps a schedule single, and for the host-wide row it is the
/// only thing that does.</b> The per-account rows are held apart by a unique index; the host-wide row
/// names no account and PostgreSQL counts NULLs as distinct, so the index cannot cover it on every
/// supported platform (<c>BackupScheduleConfiguration</c> says why). Two surfaces writing the row
/// with two copies of that lookup would be two chances to forget it.
/// </para>
/// <para>
/// Reconfigure runs for a new row too, because a new schedule is created disabled and it is this call
/// that switches it on, recording <see cref="BackupSchedule.EnabledAt"/> from which the first run is
/// the next named hour rather than the moment the form is saved.
/// </para>
/// <para>
/// This type validates nothing and authorizes nothing: who may name an account, and which values are
/// acceptable, are settled by the caller's controller policy and validator before it is reached.
/// </para>
/// </remarks>
public sealed class BackupScheduleWriter
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the writer.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="clock">The injected time source used to record when a schedule was switched on.</param>
    public BackupScheduleWriter(BackupsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Creates the account's schedule, or replaces the one it already has, and saves it.</summary>
    /// <param name="accountId">The account the schedule governs, or <c>null</c> for the host-wide policy.</param>
    /// <param name="destinationId">The destination it writes to, or <c>null</c> for this server's default one.</param>
    /// <param name="cadence">When it runs.</param>
    /// <param name="retainCount">How many successful backups to keep.</param>
    /// <param name="enabled">Whether it runs at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored schedule.</returns>
    public async Task<BackupSchedule> SaveAsync(
        Guid? accountId,
        Guid? destinationId,
        BackupCadence cadence,
        int retainCount,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cadence);

        var schedule = await FindAsync(accountId, cancellationToken);

        if (schedule is null)
        {
            schedule = new BackupSchedule(Guid.NewGuid(), accountId, destinationId, cadence, retainCount);
            _dbContext.BackupSchedules.Add(schedule);
        }

        schedule.Reconfigure(destinationId, cadence, retainCount, enabled, _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return schedule;
    }

    /// <summary>
    /// Creates or replaces one account's schedule while leaving the destination it already names where
    /// it is: the caller has no say over where an archive is written.
    /// </summary>
    /// <param name="accountId">The account the schedule governs.</param>
    /// <param name="cadence">When it runs.</param>
    /// <param name="retainCount">How many successful backups to keep.</param>
    /// <param name="enabled">Whether it runs at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The stored schedule, which names the destination an administrator gave it, or the default one
    /// (<c>null</c>) when it is new.
    /// </returns>
    public async Task<BackupSchedule> SaveKeepingDestinationAsync(
        Guid accountId,
        BackupCadence cadence,
        int retainCount,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(accountId, cancellationToken);

        return await SaveAsync(accountId, existing?.DestinationId, cadence, retainCount, enabled, cancellationToken);
    }

    /// <summary>Finds the tracked row for a scope, within the caller's tenant scope.</summary>
    /// <param name="accountId">The account, or <c>null</c> for the host-wide policy.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <c>null</c> when the scope has never been configured.</returns>
    private Task<BackupSchedule?> FindAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        return _dbContext.BackupSchedules.FirstOrDefaultAsync(row => row.AccountId == accountId, cancellationToken);
    }
}
