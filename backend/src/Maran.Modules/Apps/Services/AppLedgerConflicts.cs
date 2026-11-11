using Maran.Modules.Apps.Persistence.Configurations;
using Maran.Modules.Apps.Resources;
using Npgsql;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Names the conflict a refused ledger save means (issue #526). The ledger has three guards that refuse a
/// write that raced another: the concurrency token on the row, the unique index on the app id and the unique
/// index on the active site web server. Anything else a failed save reports is a fault and is not a conflict.
/// </summary>
public static class AppLedgerConflicts
{
    /// <summary>Reads what a failed save of the ledger means.</summary>
    /// <param name="exception">The exception the save raised.</param>
    /// <returns>
    /// The coded conflict for a lost race, <c>AppsWebServerAlreadyInstalled</c> when the loser was a second
    /// web server and <c>AppsOperationInProgress</c> otherwise; or <see langword="null"/> when the failure is
    /// not a conflict and must propagate.
    /// </returns>
    public static Error? Classify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbUpdateConcurrencyException)
        {
            return InProgress();
        }

        if (exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            return null;
        }

        return string.Equals(
            violation.ConstraintName,
            InstalledAppConfiguration.ActiveWebServerIndexName,
            StringComparison.Ordinal)
            ? Error.Of(nameof(ErrorMessages.AppsWebServerAlreadyInstalled), ErrorType.Conflict)
            : InProgress();
    }

    /// <summary>The refusal of a request that lost the race to another operation on the same app.</summary>
    /// <returns>The conflict.</returns>
    private static Error InProgress()
    {
        return Error.Of(nameof(ErrorMessages.AppsOperationInProgress), ErrorType.Conflict);
    }
}
