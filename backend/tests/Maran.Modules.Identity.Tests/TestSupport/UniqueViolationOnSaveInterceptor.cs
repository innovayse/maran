using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// A save interceptor that makes the first save fail the way PostgreSQL fails one that loses a race
/// on a unique index: a <see cref="DbUpdateException"/> wrapping a <see cref="PostgresException"/>
/// with the unique-violation state. The in-memory provider has no unique indexes, so this is the only
/// way a unit test can reach the handlers' unique-violation branch.
/// </summary>
public sealed class UniqueViolationOnSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>Runs just before the failure is raised, so a test can store the row the "winner" wrote.</summary>
    private readonly Action? _beforeFailing;

    /// <summary>Whether the failure has already been raised; only the first save fails.</summary>
    private bool _failed;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="beforeFailing">What the winning writer stored before this save lost, or null for nothing.</param>
    public UniqueViolationOnSaveInterceptor(Action? beforeFailing = null)
    {
        _beforeFailing = beforeFailing;
    }

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
        _beforeFailing?.Invoke();
        throw new DbUpdateException(
            "simulated unique violation",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
    }
}
