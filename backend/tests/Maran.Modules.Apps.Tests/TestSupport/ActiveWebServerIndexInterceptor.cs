using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>
/// Stands in for the partial unique index on the active site web server, which the in-memory provider does
/// not enforce. It judges one save the way the database may apply it, inserts and updates before deletes:
/// a row that stops being active in the same save as another becomes active still holds its place, so two
/// such rows are refused with the unique violation PostgreSQL raises.
/// </summary>
public sealed class ActiveWebServerIndexInterceptor : SaveChangesInterceptor
{
    /// <summary>Counts the active web server places a save would hold at its worst moment.</summary>
    /// <param name="context">The context being saved.</param>
    /// <returns>The number of rows holding a place.</returns>
    private static int PlacesHeld(DbContext context)
    {
        var held = 0;
        foreach (var entry in context.ChangeTracker.Entries<InstalledApp>())
        {
            var wasActive = entry.State != EntityState.Added && IsActive(entry.Property(app => app.State).OriginalValue, entry.Entity.Kind);
            var isActive = entry.State != EntityState.Deleted && IsActive(entry.Entity.State, entry.Entity.Kind);
            if (wasActive || isActive)
            {
                held++;
            }
        }

        return held;
    }

    /// <summary>Whether a web server in a state holds the ports.</summary>
    /// <param name="state">The row's state.</param>
    /// <param name="kind">The row's kind.</param>
    /// <returns>True for a web server that is installing, installed or being removed.</returns>
    private static bool IsActive(AppState state, AppKind kind)
    {
        return kind == AppKind.WebServer && state is AppState.Installing or AppState.Installed or AppState.Removing;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && PlacesHeld(eventData.Context) > 1)
        {
            throw new DbUpdateException(
                "duplicate key value violates unique constraint",
                new PostgresException(
                    "duplicate key value violates unique constraint",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation,
                    constraintName: InstalledAppConfiguration.ActiveWebServerIndexName));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
