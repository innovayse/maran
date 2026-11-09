using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// Reads the flat frequency, weekday and hour a request carries as the one cadence they name.
/// </summary>
/// <remarks>
/// Both schedule surfaces, the administrator's and the customer's, bind the same three fields, so the
/// reading lives here once. A command dispatched on the bus is validated too, but a cadence cannot be
/// built from a weekly pair with no weekday or a daily pair with one, so the refusal is stated here
/// rather than a reading being guessed.
/// </remarks>
public static class BackupCadenceTranslator
{
    /// <summary>Builds the cadence a request names.</summary>
    /// <param name="frequency">How often the backup is taken.</param>
    /// <param name="hourUtc">The hour of the day, in UTC.</param>
    /// <param name="dayOfWeekUtc">The weekday of a weekly schedule; must be absent for a daily one.</param>
    /// <returns>The cadence, or <c>BackupScheduleDayRequired</c> / <c>BackupScheduleDayNotAllowed</c>.</returns>
    public static Result<BackupCadence> From(BackupFrequency frequency, int hourUtc, DayOfWeek? dayOfWeekUtc)
    {
        if (frequency == BackupFrequency.Weekly)
        {
            if (dayOfWeekUtc is null)
            {
                return Result<BackupCadence>.Fail(
                    Error.Of(nameof(ErrorMessages.BackupScheduleDayRequired), ErrorType.Validation));
            }

            return Result<BackupCadence>.Ok(new WeeklyBackupCadence(dayOfWeekUtc.Value, hourUtc));
        }

        if (dayOfWeekUtc is not null)
        {
            return Result<BackupCadence>.Fail(
                Error.Of(nameof(ErrorMessages.BackupScheduleDayNotAllowed), ErrorType.Validation));
        }

        return Result<BackupCadence>.Ok(new DailyBackupCadence(hourUtc));
    }
}
