using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// Refuses a save with a <see cref="DbUpdateException"/> while armed, so a test can reach the point where
/// the host accepted a change and the database did not. The in-memory provider cannot fail a save on its
/// own; in production this is a dropped connection or a timeout.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>Whether the next save is refused.</summary>
    public bool Armed { get; set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Armed)
        {
            throw new DbUpdateException("The save was refused by the test.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
