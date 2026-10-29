using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An EF Core save interceptor that throws one arranged exception on the next save and then
/// disarms itself, so a test can put a plan handler on the path it takes when the database refuses
/// a write that raced another request (a unique or foreign-key violation).
/// </summary>
/// <remarks>
/// The in-memory provider these tests run on enforces no unique index or foreign key and raises no
/// <c>PostgresException</c>, so the race branches are otherwise unobservable here. Interception is
/// EF Core's supported seam for it and leaves the sealed context untouched. It disarms after one
/// throw so the same context can still be read afterwards.
/// </remarks>
public sealed class ArmedSaveFailureInterceptor : SaveChangesInterceptor
{
    /// <summary>The exception the next save throws, or null to let saves through.</summary>
    /// <remarks>
    /// Assigned by the test immediately before the act, never in a fixture: the arrangement of a
    /// test's own rows goes through the same save path and would otherwise be the thing that failed.
    /// </remarks>
    public Exception? Next
    {
        get;
        set;
    }

    /// <summary>Throws the arranged exception, once, before the save is executed.</summary>
    /// <param name="eventData">EF Core's context for the save being intercepted.</param>
    /// <param name="result">The interception result EF Core proposes.</param>
    /// <param name="cancellationToken">Cancellation token for the save.</param>
    /// <returns>The unchanged interception result when nothing is armed.</returns>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Next is not null)
        {
            var arranged = Next;
            Next = null;

            throw arranged;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
