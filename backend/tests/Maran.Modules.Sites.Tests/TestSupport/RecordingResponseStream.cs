using System.Text;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// A response body that records what was written to it, readable while writing continues.
/// </summary>
/// <remarks>
/// A <see cref="MemoryStream"/> would do for a finished stream, but not for a live one: a test that
/// polls for a heartbeat reads the body while the writer is still appending to it, and reading a
/// MemoryStream's buffer concurrently with a write is undefined. Everything here happens under one
/// lock, so the text a test reads is always a whole prefix of what was written.
/// </remarks>
public sealed class RecordingResponseStream : Stream
{
    /// <summary>Guards <see cref="_written"/> against the writer and the reader at once.</summary>
    private readonly Lock _gate = new();

    /// <summary>Everything written so far.</summary>
    private readonly StringBuilder _written = new();

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
            return Text.Length;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            return Text.Length;
        }

        set
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Everything written so far, decoded as UTF-8 text.</summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _written.ToString();
            }
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        Append(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Append(buffer);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Append(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Append(new ReadOnlySpan<byte>(buffer, offset, count));
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

    /// <summary>Appends one written span to the record.</summary>
    /// <param name="buffer">The bytes written.</param>
    private void Append(ReadOnlySpan<byte> buffer)
    {
        lock (_gate)
        {
            _written.Append(Encoding.UTF8.GetString(buffer));
        }
    }
}
