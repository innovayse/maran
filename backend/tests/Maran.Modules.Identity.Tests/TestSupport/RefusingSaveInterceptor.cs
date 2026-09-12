using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Refuses every save that removes a row, so a handler's behaviour when a deletion cannot be
/// committed can be observed while the seeding that sets the test up still succeeds.
/// </summary>
/// <remarks>
/// <para>
/// An interceptor rather than a derived context because <c>IdentityDbContext</c> is sealed, and
/// rather than a hand-rolled double because the point of the tests that use it is that the REAL
/// context is what refuses — a double would prove only that a double throws.
/// </para>
/// <para>
/// It refuses on the DELETED entries rather than on every save because the alternative — a flag the
/// test arms between seeding and acting — puts the moment of arming in the test body, where it is
/// one forgotten line away from a test that passes because nothing was ever refused.
/// </para>
/// </remarks>
public sealed class RefusingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>The message the refusal carries, so a test can name what it caught.</summary>
    public const string Message = "the database refused to remove these rows";

    /// <summary>Refuses an asynchronous save that removes rows, before it reaches the store.</summary>
    /// <param name="eventData">What EF Core is about to save.</param>
    /// <param name="result">The interception result EF Core would otherwise use.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The unchanged result when the save removes nothing; otherwise it throws.</returns>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var removing = eventData.Context is not null
            && eventData.Context.ChangeTracker.Entries().Any(entry => { return entry.State == EntityState.Deleted; });
        if (removing)
        {
            throw new InvalidOperationException(Message);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
