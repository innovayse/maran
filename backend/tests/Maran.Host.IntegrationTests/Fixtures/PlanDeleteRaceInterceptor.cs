using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An EF Core save interceptor that runs one arranged action immediately before the first save it
/// sees, so a test can land a competing write in the gap between a handler's check and its save —
/// the race a real second request would win, produced deterministically instead of by timing.
/// </summary>
/// <remarks>
/// The action runs once and the interceptor disarms before running it, so a save the action itself
/// performs (through another context carrying this same interceptor) passes straight through
/// instead of recursing.
/// </remarks>
public sealed class PlanDeleteRaceInterceptor : SaveChangesInterceptor
{
    /// <summary>The competing write to perform before the next save, or null once it has run.</summary>
    private Func<Task>? _competingWrite;

    /// <summary>Arms the interceptor with the write to land before the next save.</summary>
    /// <param name="competingWrite">The competing write.</param>
    public void Arm(Func<Task> competingWrite)
    {
        _competingWrite = competingWrite;
    }

    /// <summary>Runs the armed write once, then lets the save proceed.</summary>
    /// <param name="eventData">EF Core's context for the save being intercepted.</param>
    /// <param name="result">The interception result EF Core proposes.</param>
    /// <param name="cancellationToken">Cancellation token for the save.</param>
    /// <returns>The unchanged interception result.</returns>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var write = _competingWrite;
        _competingWrite = null;
        if (write is not null)
        {
            await write();
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
