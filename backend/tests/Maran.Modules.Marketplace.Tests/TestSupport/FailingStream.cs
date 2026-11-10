namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A stream that yields some bytes and then fails, as a connection dropped mid-download does.</summary>
public sealed class FailingStream : Stream
{
    /// <summary>The bytes yielded before the failure.</summary>
    private readonly byte[] _bytes;

    /// <summary>How many bytes were handed out.</summary>
    private int _position;

    /// <summary>Creates the stream.</summary>
    /// <param name="bytes">The bytes yielded before the failure.</param>
    public FailingStream(byte[] bytes)
    {
        _bytes = bytes;
    }

    /// <inheritdoc />
    public override bool CanRead
    {
        get
        {
            return true;
        }
    }

    /// <inheritdoc />
    public override bool CanSeek
    {
        get
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override bool CanWrite
    {
        get
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            throw new NotSupportedException();
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            throw new NotSupportedException();
        }
        set
        {
            throw new NotSupportedException();
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _bytes.Length)
        {
            throw new IOException("The connection dropped.");
        }

        var count = Math.Min(buffer.Length, _bytes.Length - _position);
        _bytes.AsMemory(_position, count).CopyTo(buffer);
        _position += count;
        return ValueTask.FromResult(count);
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
