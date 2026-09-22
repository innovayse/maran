namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Serializes the verify-then-write sequence <c>Commands.InstallLicence.InstallLicenceCommandHandler</c>
/// runs, so two concurrent installs can never race two temp-file writes against the one
/// <c>rename()</c> target.
/// </summary>
/// <remarks>
/// A single in-process lock over a single global resource — the one licence file — per
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c> §4's own conclusion:
/// this endpoint has no reason to be highly concurrent, and the panel's own multi-instance story (if
/// any) was NOT FOUND in the tree when that note was written, so a per-process
/// <see cref="SemaphoreSlim"/> is the design this slice implements; a future multi-replica deployment
/// would need a distributed lock instead, which is out of this slice's scope.
/// </remarks>
public sealed class LicenceInstallLock : IDisposable
{
    /// <summary>The one shared gate: at most one install runs its verify-then-write sequence at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Runs <paramref name="action"/> with exclusive access to the licence file.</summary>
    /// <typeparam name="TResult">What <paramref name="action"/> returns.</typeparam>
    /// <param name="action">The verify-then-write sequence to run exclusively.</param>
    /// <param name="cancellationToken">Cancellation token for waiting on the gate and for <paramref name="action"/>.</param>
    /// <returns>Whatever <paramref name="action"/> returns.</returns>
    public async Task<TResult> RunExclusiveAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the underlying gate. Safe to call once, on shutdown — this type is registered as a singleton.</summary>
    public void Dispose()
    {
        _gate.Dispose();
    }
}
