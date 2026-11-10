namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A readable in-memory stream that remembers whether it was disposed, so a test can prove the Host
/// closes every file the screen-bundle window opened for it.
/// </summary>
public sealed class DisposalTrackingStream : MemoryStream
{
    /// <summary>Whether the stream has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Creates the stream over some bytes.</summary>
    /// <param name="bytes">The file's content.</param>
    public DisposalTrackingStream(byte[] bytes)
        : base(bytes, writable: false)
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
