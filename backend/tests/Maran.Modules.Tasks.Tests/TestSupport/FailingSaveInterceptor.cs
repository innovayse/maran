using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// Makes saves on a context throw a chosen exception, so a test can kill a recorder's writes and
/// watch the operation it was recording carry on regardless.
/// </summary>
/// <remarks>
/// <para>
/// An EF Core interceptor rather than a subclassed context, because <c>TasksDbContext</c> is sealed
/// and must stay sealed: unsealing production code so a test can override a method makes the test's
/// needs visible in the shipped type, and the next reader cannot tell an extension point from a
/// concession. An interceptor is EF's own supported seam and touches nothing.
/// </para>
/// <para>
/// It fails EVERY save from a chosen point on, rather than one and then healing. A one-shot version
/// of this fixture cannot exercise the recorder's wrap at all: the first failure is the one that
/// makes <c>BeginAsync</c> answer the empty id, after which every later call short-circuits without
/// touching the database, so three of the four wrapped methods would be named by the test and never
/// run. Arming it AFTER the opening save is what puts a real failure under a real report.
/// </para>
/// </remarks>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    /// <summary>The failure to raise once the allowance is spent.</summary>
    private readonly Exception _failure;

    /// <summary>How many saves still succeed before the failures start.</summary>
    private int _remainingSuccesses;

    /// <summary>Arms the interceptor.</summary>
    /// <param name="failure">What a failing save throws.</param>
    /// <param name="afterSaves">How many saves succeed first; zero fails the very first one.</param>
    public FailingSaveInterceptor(Exception failure, int afterSaves = 0)
    {
        _failure = failure;
        _remainingSuccesses = afterSaves;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_remainingSuccesses > 0)
        {
            _remainingSuccesses -= 1;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        throw _failure;
    }
}
