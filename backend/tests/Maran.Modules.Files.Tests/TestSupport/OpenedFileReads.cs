using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>Builds the stream the agent hands back for a read, from chunks a test chooses.</summary>
/// <remarks>
/// The stream has no public constructor; the Agent.Client project lets this test project reach the
/// internal one, so a handler's behaviour on a stream that ends short or that fails half way is observed
/// on a stream that really does.
/// </remarks>
public static class OpenedFileReads
{
    /// <summary>A stream that delivers the chunks and ends.</summary>
    /// <param name="chunks">The bytes of each message, in order.</param>
    /// <returns>The stream, with its first message already read as the agent client does.</returns>
    public static async Task<OpenedFileRead> OfAsync(params byte[][] chunks)
    {
        return await OpenAsync(Messages(chunks));
    }

    /// <summary>A stream that delivers the chunks and then ends with the agent's refusal.</summary>
    /// <param name="chunks">The bytes of each message before the refusal.</param>
    /// <returns>The stream.</returns>
    public static async Task<OpenedFileRead> FailingAfterAsync(params byte[][] chunks)
    {
        var refusal = new ReadFileResponse
        {
            Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "read failed" },
        };

        return await OpenAsync(Messages(chunks).Append(refusal));
    }

    /// <summary>Opens a stream over messages, reading the first as the agent client does before it hands the stream out.</summary>
    /// <param name="messages">The messages the agent sends.</param>
    /// <returns>The stream.</returns>
    private static async Task<OpenedFileRead> OpenAsync(IEnumerable<ReadFileResponse> messages)
    {
        var enumerator = AsAsync(messages).GetAsyncEnumerator();
        var first = await enumerator.MoveNextAsync() ? enumerator.Current : null;

        return new OpenedFileRead(enumerator, first, NullLogger.Instance);
    }

    /// <summary>One data message per chunk.</summary>
    /// <param name="chunks">The bytes of each message.</param>
    /// <returns>The messages.</returns>
    private static IEnumerable<ReadFileResponse> Messages(byte[][] chunks)
    {
        return chunks.Select(chunk =>
        {
            return new ReadFileResponse { Ok = new ReadFileChunk { Data = Google.Protobuf.ByteString.CopyFrom(chunk) } };
        });
    }

    /// <summary>Exposes a sequence as an asynchronous one.</summary>
    /// <param name="messages">The messages.</param>
    /// <returns>The same messages as an async stream.</returns>
    private static async IAsyncEnumerable<ReadFileResponse> AsAsync(IEnumerable<ReadFileResponse> messages)
    {
        foreach (var message in messages)
        {
            await Task.Yield();
            yield return message;
        }
    }
}
