using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// A save interceptor that makes the first save fail the way PostgreSQL fails one that collides on a
/// key: a <see cref="DbUpdateException"/> wrapping a <see cref="PostgresException"/> with the
/// unique-violation state. The in-memory provider has no key enforcement across contexts, so this is the
/// only way a unit test can reach a handler's duplicate-delivery branch.
/// </summary>
public sealed class UniqueViolationOnSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>Whether the failure has already been raised; only the first save fails.</summary>
    private bool _failed;

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_failed)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        _failed = true;
        throw new DbUpdateException(
            "simulated unique violation",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
    }
}
