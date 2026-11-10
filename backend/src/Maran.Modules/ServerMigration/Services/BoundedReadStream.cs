namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Reads at most a fixed number of bytes from a stream and then reports its end, so an upload is relayed to the
/// agent for exactly the length its signed manifest declared (issue #127).
/// </summary>
/// <remarks>
/// The bytes after that length are left in the underlying stream untouched, which is how the caller finds out the
/// package carried more than it signed. Forward-only and read-only: nothing here buffers the artifact.
/// </remarks>
public sealed class BoundedReadStream : Stream
{
    /// <summary>The stream being read.</summary>
    private readonly Stream _inner;

    /// <summary>How many bytes may still be read.</summary>
    private long _remaining;

    /// <summary>Wraps a stream.</summary>
    /// <param name="inner">The stream to read from.</param>
    /// <param name="length">The most bytes this wrapper will yield.</param>
    public BoundedReadStream(Stream inner, long length)
    {
        _inner = inner;
        _remaining = length;
    }

    /// <inheritdoc />
    public override bool CanRead
    {
        get { return true; }
    }

    /// <inheritdoc />
    public override bool CanSeek
    {
        get { return false; }
    }

    /// <inheritdoc />
    public override bool CanWrite
    {
        get { return false; }
    }

    /// <inheritdoc />
    public override long Length
    {
        get { throw new NotSupportedException(); }
    }

    /// <inheritdoc />
    public override long Position
    {
        get { throw new NotSupportedException(); }
        set { throw new NotSupportedException(); }
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        var window = buffer.Length > _remaining ? buffer[..(int)_remaining] : buffer;
        var read = await _inner.ReadAsync(window, cancellationToken);
        _remaining -= read;

        return read;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
