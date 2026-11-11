using Maran.Modules.Apps.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>Refuses the first save with a lost-race conflict and lets every later one through, as a rival writer that has finished.</summary>
public sealed class ConflictOnceSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>The number of saves refused so far.</summary>
    public int Refused { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Refused == 0)
        {
            Refused++;
            throw new DbUpdateException(
                "duplicate key value violates unique constraint",
                new PostgresException(
                    "duplicate key value violates unique constraint",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation,
                    constraintName: InstalledAppConfiguration.AppIdIndexName));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
