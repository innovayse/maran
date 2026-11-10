namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// An open package download: the body stream and the length the host declared. It owns whatever keeps
/// the connection open, so disposing it releases the connection whether or not the stream was read to
/// the end.
/// </summary>
public sealed class MarketplaceDownload : IDisposable
{
    /// <summary>What keeps the connection behind <see cref="Body"/> open.</summary>
    private readonly IDisposable _owner;

    /// <summary>The body stream, positioned at the first byte.</summary>
    public Stream Body { get; }

    /// <summary>The length the host declared, or null when it declared none.</summary>
    public long? ContentLength { get; }

    /// <summary>Wraps an open download.</summary>
    /// <param name="body">The body stream, positioned at the first byte.</param>
    /// <param name="contentLength">The length the host declared, or null when it declared none.</param>
    /// <param name="owner">Released together with the stream.</param>
    public MarketplaceDownload(Stream body, long? contentLength, IDisposable owner)
    {
        Body = body;
        ContentLength = contentLength;
        _owner = owner;
    }

    /// <summary>Releases the stream and the connection behind it.</summary>
    public void Dispose()
    {
        Body.Dispose();
        _owner.Dispose();
    }
}
