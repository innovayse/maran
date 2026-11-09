using Grpc.Core;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>
/// A file the agent has agreed to stream, opened and not yet read: its first chunk, or its refusal,
/// has already arrived, so a file over the size ceiling is a failed <see cref="Result{T}"/> before
/// any byte is held.
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns the call.</b> Disposing it disposes the underlying gRPC call, which cancels a stream
/// not yet read to its end, so a browser that stops a download stops the agent reading the file. It
/// must be disposed whether or not it was read.
/// </para>
/// <para>
/// <b>The byte count is taken here.</b> <see cref="CopyToAsync"/> returns the bytes it wrote to the
/// destination, counted as they pass, and a caller that must enforce a ceiling of its own reads that
/// count and not a length the agent or the browser claimed.
/// </para>
/// <para>
/// <b>A failure after the first byte is a failed result and the body is void.</b> The agent ends a
/// stream with an error when a file grows past the ceiling while it is read, and the transport can
/// fail mid-stream; both come back as a failed <see cref="Result{T}"/> and the caller MUST discard
/// what was already written to the destination. A caller that has already sent response headers
/// cannot send a problem document and must abort the connection instead.
/// </para>
/// <para>
/// The content is never logged. The token given to <see cref="CopyToAsync"/> must be the one the
/// stream was opened with: the call itself is bound to that one, and a second token would make a
/// cancellation of the first read as a failure of the agent. A stream is read once; a second <see cref="CopyToAsync"/> is a
/// defect of the caller and raises.
/// </para>
/// </remarks>
public sealed class OpenedFileRead : IAsyncDisposable
{
    /// <summary>The agent's messages, positioned after the first one.</summary>
    private readonly IAsyncEnumerator<ReadFileResponse> _responses;

    /// <summary>Where the agent's own diagnostic text goes if the stream ends in an error.</summary>
    private readonly ILogger _logger;

    /// <summary>The first message read when the stream was opened, or null when the file had none.</summary>
    private readonly ReadFileResponse? _first;

    /// <summary>Whether <see cref="CopyToAsync"/> has been called.</summary>
    private bool _consumed;

    /// <summary>Whether <see cref="DisposeAsync"/> has run.</summary>
    private bool _disposed;

    /// <summary>Wraps an opened stream.</summary>
    /// <param name="responses">The agent's messages, already advanced past <paramref name="first"/>.</param>
    /// <param name="first">The first message, a chunk, or null for a file the agent streamed no byte of.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal OpenedFileRead(
        IAsyncEnumerator<ReadFileResponse> responses,
        ReadFileResponse? first,
        ILogger logger)
    {
        _responses = responses;
        _first = first;
        _logger = logger;
    }

    /// <summary>Copies the whole file to <paramref name="destination"/>.</summary>
    /// <param name="destination">Where the bytes go; the caller owns and disposes it.</param>
    /// <param name="cancellationToken">Cancellation; a cancellation the caller asked for is raised, not returned.</param>
    /// <returns>
    /// The number of bytes written to <paramref name="destination"/>, or the typed failure the agent
    /// or the transport ended the stream with, in which case the bytes already written are void.
    /// </returns>
    public async Task<Result<ulong>> CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_consumed)
        {
            throw new InvalidOperationException("A file stream is read once.");
        }

        _consumed = true;
        ulong written = 0;
        var current = _first;

        while (current is not null)
        {
            if (current.ResultCase != ReadFileResponse.ResultOneofCase.Ok)
            {
                return Result<ulong>.Fail(current.ResultCase == ReadFileResponse.ResultOneofCase.Error
                    ? AgentErrorTranslator.ToError(_logger, current.Error, "ReadFileAsync")
                    : Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            var data = current.Ok.Data.Memory;
            await destination.WriteAsync(data, cancellationToken);
            written += (ulong)data.Length;

            var next = await NextAsync(cancellationToken);
            if (next.Failure is not null)
            {
                return Result<ulong>.Fail(next.Failure);
            }

            current = next.Message;
        }

        return Result<ulong>.Ok(written);
    }

    /// <summary>Disposes the stream, and with it the call to the agent.</summary>
    /// <returns>A task that completes once the call is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _responses.DisposeAsync();
    }

    /// <summary>
    /// Reads the next message, answering a transport that gave up as a typed failure; a cancellation
    /// the caller asked for is raised.
    /// </summary>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The next message, null at the end of the stream, or the failure.</returns>
    private async Task<(ReadFileResponse? Message, Error? Failure)> NextAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _responses.MoveNextAsync())
            {
                return (_responses.Current, null);
            }

            return (null, null);
        }
        catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, AgentTransportFailure.ToError(exception));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The channel reports a connection torn mid-stream as a cancellation nobody asked for
            // (see AgentStreamGuard), so it takes the answer a cancelled status takes.
            return (null, AgentTransportFailure.ToError(new RpcException(new Status(StatusCode.Cancelled, string.Empty))));
        }
    }
}
