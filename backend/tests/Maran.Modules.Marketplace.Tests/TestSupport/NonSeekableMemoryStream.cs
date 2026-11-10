namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// A readable stream that reports it cannot seek, so HTTP content built over it declares no length and the
/// reader has only the bytes to go by, which is how a host that lies about, or omits, a length behaves.
/// </summary>
public sealed class NonSeekableMemoryStream : MemoryStream
{
    /// <summary>Creates the stream over fixed bytes.</summary>
    /// <param name="bytes">What the stream yields.</param>
    public NonSeekableMemoryStream(byte[] bytes)
        : base(bytes, writable: false)
    {
    }

    /// <inheritdoc />
    public override bool CanSeek
    {
        get
        {
            return false;
        }
    }
}
