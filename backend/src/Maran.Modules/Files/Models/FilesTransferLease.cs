namespace Maran.Modules.Files.Models;

/// <summary>
/// A transfer slot that is held until it is disposed. Disposal is idempotent, so a lease released by a
/// <c>finally</c> and again by a cancelled stream's cleanup gives back one slot, not two.
/// </summary>
public sealed class FilesTransferLease : IDisposable
{
    /// <summary>Gives the slot back; swapped out on first use so a second disposal does nothing.</summary>
    private Action? _release;

    /// <summary>Creates a lease.</summary>
    /// <param name="release">Gives the slot back to the counter it was taken from.</param>
    public FilesTransferLease(Action release)
    {
        _release = release;
    }

    /// <summary>Gives the slot back, once.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
