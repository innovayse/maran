namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// A response body that accepts a fixed number of writes and then fails the way a torn-down
/// connection does.
/// </summary>
/// <remarks>
/// It is how "the client has gone" is made observable in a unit test. The real failure is a socket
/// the browser closed, which surfaces as an <see cref="IOException"/> out of the write; anything the
/// writer does after that is work nobody is waiting for.
/// </remarks>
public sealed class FailingResponseStream : Stream
{
    /// <summary>How many writes succeed before the connection is treated as gone.</summary>
    private readonly int _allowedWrites;

    /// <summary>How many writes have been attempted.</summary>
    private int _writes;

    /// <summary>Creates the stream.</summary>
    /// <param name="allowedWrites">How many writes succeed before every later one throws.</param>
    public FailingResponseStream(int allowedWrites)
    {
        _allowedWrites = allowedWrites;
    }

    /// <inheritdoc />
    public override bool CanRead
    {
        get
        {
            return false;
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
            return true;
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
    public override void Write(byte[] buffer, int offset, int count)
    {
        Count();
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Count();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Count();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
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

    /// <summary>Counts one write, failing once the allowance is spent.</summary>
    private void Count()
    {
        _writes++;
        if (_writes > _allowedWrites)
        {
            throw new IOException("The client has gone.");
        }
    }
}
