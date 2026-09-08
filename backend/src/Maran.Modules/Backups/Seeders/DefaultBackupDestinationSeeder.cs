using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;

namespace Maran.Modules.Backups.Seeders;

/// <summary>
/// Makes sure this server has the one destination every backup is written to, and that the row
/// states no directory the panel cannot establish.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row is an identity, not a location.</b> It exists so a backup can POINT at a destination
/// and a screen can name one; where a local destination's archives actually rest is the agent's
/// constant, which only the agent can state and which the destinations query reads from its
/// handshake at the moment it is shown. This seeder therefore writes no path, and clears the one a
/// server booted before 2026-09-08 recorded from the removed <c>Backups__LocalRoot</c> setting —
/// that value was a copy the panel had no way to check, and it was wrong on any server whose
/// operator had set it.
/// </para>
/// <para>
/// <b>It does not ask whether the list is empty.</b> That shape shipped once in this repository and
/// is recorded as a defect: an administrator who deleted the seeded row emptied the list, so the next
/// restart restored something that had been deliberately revoked. This keys on a FIXED identity
/// instead — and the destinations surface publishes no delete, so the row cannot be absent for any
/// reason except a server that has not booted yet.
/// </para>
/// <para>
/// <b>It needs no configuration and can refuse nothing.</b> The startup validator that used to
/// approve a configured root is gone with the setting: there is no operator-supplied path left for
/// this module to accept or turn away, which is the only shape in which the panel cannot state a
/// directory that is not the agent's.
/// </para>
/// </remarks>
public sealed class DefaultBackupDestinationSeeder
{
    /// <summary>The label the default destination carries until an operator has any way to change it.</summary>
    /// <remarks>
    /// English and not localized, because it is stored data rather than a message: a row's name has
    /// to mean the same thing to the operator who reads it in <c>psql</c> and to every screen, and a
    /// value translated at write time would be frozen in whichever language the server first booted
    /// in.
    /// </remarks>
    public const string DefaultDestinationName = "Local storage";

    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The injected time source used to stamp a newly created row.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="clock">The injected time source.</param>
    public DefaultBackupDestinationSeeder(BackupsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Creates the default destination if it is absent, and clears any path it carries.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row states no directory the panel cannot establish.</returns>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var destination = await _dbContext.BackupDestinations
            .FirstOrDefaultAsync(row => row.Id == BackupDestination.DefaultDestinationId, cancellationToken);

        if (destination is null)
        {
            _dbContext.BackupDestinations.Add(new BackupDestination(
                BackupDestination.DefaultDestinationId,
                DefaultDestinationName,
                BackupDestinationKind.Local,
                string.Empty,
                isDefault: true,
                _clock.UtcNow));
        }
        else
        {
            destination.ClearRecordedPath();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
