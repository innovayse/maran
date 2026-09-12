using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An EF Core save interceptor that throws one arranged exception on the next save and then
/// disarms itself, so a test can put a handler on the path it takes when the database refuses a
/// write it has already made a host-side change for.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the two branches it reaches are otherwise UNOBSERVABLE here.
/// <c>CreateFtpUserCommandHandler</c> distinguishes a unique-violation
/// (<c>SqlState 23505</c>, meaning a concurrent creation won and owns the login) from every other
/// database failure (meaning nothing owns it, so the login must be removed from the host again) —
/// and the in-memory provider these tests run on enforces no unique index and raises no
/// <c>PostgresException</c>, so neither condition can be provoked by arranging rows. Without this,
/// both branches sit in production code that no test can execute, which is the shape
/// rules/testing.md calls a check that cannot observe what it reports on.
/// </para>
/// <para>
/// It is an interceptor rather than a <c>FtpDbContext</c> subclass on purpose: the context is
/// <c>sealed</c> by the repository's own rule, and unsealing production code to make a test
/// possible would change the shipped design to suit the test. Interception is EF Core's supported
/// seam for exactly this and leaves the context untouched.
/// </para>
/// <para>
/// It disarms after one throw so that a test can arm it, watch the handler fail, and still use the
/// same context afterwards — a permanently armed double would make every later assertion about the
/// database throw instead of answering.
/// </para>
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
