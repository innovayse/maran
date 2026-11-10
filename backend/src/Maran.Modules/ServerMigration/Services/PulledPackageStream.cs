namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// The body of the other panel's answer, read forward only, with a deadline on every wait for the next bytes and
/// the response released when it is disposed (issue #127).
/// </summary>
/// <remarks>
/// A package of hundreds of gigabytes legitimately takes hours, so the body as a whole has no time limit: the
/// signed length bounds how much is read, and this ends a peer that has stopped sending. A read that waits longer
/// than the stall deadline raises <see cref="TimeoutException"/>, which the caller turns into the unreachable
/// failure. Nothing here buffers.
/// </remarks>
public sealed class PulledPackageStream : Stream
{
    /// <summary>The response whose body is read; released with this stream.</summary>
    private readonly HttpResponseMessage _response;

    /// <summary>The response's body.</summary>
    private readonly Stream _body;

    /// <summary>The longest one read may wait.</summary>
    private readonly TimeSpan _stall;

    /// <summary>Wraps a response whose headers have arrived.</summary>
    /// <param name="response">The response; disposed with this stream.</param>
    /// <param name="body">The response's body.</param>
    /// <param name="stall">The longest one read may wait for the next bytes.</param>
    public PulledPackageStream(HttpResponseMessage response, Stream body, TimeSpan stall)
    {
        _response = response;
        _body = body;
        _stall = stall;
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_stall);

        try
        {
            return await _body.ReadAsync(buffer, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The other server stopped sending.");
        }
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }
}
