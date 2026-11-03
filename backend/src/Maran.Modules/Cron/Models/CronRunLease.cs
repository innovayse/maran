namespace Maran.Modules.Cron.Models;

/// <summary>
/// One taken run slot (issue #333): disposing it gives the slot back, exactly once.
/// </summary>
/// <remarks>
/// A handler holds it in a <c>using</c> around the agent call, so every way out of the call — the
/// answer, a refusal, a cancelled request, an exception — returns the slot. A slot that leaked would
/// shrink the bound until the account could no longer run anything.
/// </remarks>
public sealed class CronRunLease : IDisposable
{
    /// <summary>Gives the slot back; cleared on first use so a second dispose does nothing.</summary>
    private Action? _release;

    /// <summary>Creates a lease over a slot that <paramref name="release"/> gives back.</summary>
    /// <param name="release">Returns the slot to the counter it was taken from.</param>
    public CronRunLease(Action release)
    {
        _release = release;
    }

    /// <summary>Gives the slot back; calling it again does nothing.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
