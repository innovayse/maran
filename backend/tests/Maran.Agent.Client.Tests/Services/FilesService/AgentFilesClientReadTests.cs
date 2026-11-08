using Google.Protobuf;
using Grpc.Core;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FilesService;

/// <summary>The streaming read of AgentFilesClient: opening, copying, counting, refusing and releasing the call.</summary>
public sealed class AgentFilesClientReadTests
{
    /// <summary>A read sends the account, the path and the byte ceiling to the agent.</summary>
    [Fact]
    public async Task A_read_sends_the_account_the_path_and_the_byte_ceiling()
    {
        var stub = StubWith(Chunk("x"));

        await using var stream = (await ClientFor(stub).ReadFileAsync("acct", "a/b.txt", 1_024, default)).Value;

        var request = Assert.IsType<ReadFileRequest>(stub.LastReadRequest);
        Assert.Equal("acct", request.AccountUsername);
        Assert.Equal("a/b.txt", request.Path);
        Assert.Equal(1_024ul, request.MaxBytes);
    }

    /// <summary>The bytes of every chunk reach the destination in order and the count is the bytes written.</summary>
    [Fact]
    public async Task The_bytes_of_every_chunk_reach_the_destination_in_order_and_are_counted()
    {
        var stub = StubWith(Chunk("hello "), Chunk("wor"), Chunk("ld"));
        var opened = await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, default);
        using var destination = new MemoryStream();

        await using var stream = opened.Value;
        var copied = await stream.CopyToAsync(destination, default);

        Assert.True(copied.IsSuccess);
        Assert.Equal(11ul, copied.Value);
        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(destination.ToArray()));
    }

    /// <summary>A file the agent streams no byte of is an empty body and a success.</summary>
    [Fact]
    public async Task A_file_with_no_message_is_an_empty_body_and_a_success()
    {
        var opened = await ClientFor(StubWith()).ReadFileAsync("acct", "empty.txt", 100, default);
        using var destination = new MemoryStream();

        await using var stream = opened.Value;
        var copied = await stream.CopyToAsync(destination, default);

        Assert.True(copied.IsSuccess);
        Assert.Equal(0ul, copied.Value);
        Assert.Empty(destination.ToArray());
    }

    /// <summary>A file over the ceiling is refused at open, before any byte is held, with its own code.</summary>
    [Fact]
    public async Task A_file_over_the_ceiling_is_refused_at_open_with_its_own_code()
    {
        var stub = StubWith(ErrorMessage(ErrorCode.TooLarge, "/home/acct/big.iso is 900 MiB"));

        var opened = await ClientFor(stub).ReadFileAsync("acct", "big.iso", 100, default);

        Assert.False(opened.IsSuccess);
        Assert.Equal("AgentTooLarge", opened.Error!.Code);
        Assert.Equal(ErrorType.Validation, opened.Error.Type);
        Assert.True(stub.ReadDisposed);
    }

    /// <summary>An entry the agent will not open maps to a code that carries none of the agent's text.</summary>
    [Fact]
    public async Task An_entry_the_agent_will_not_open_maps_to_a_code_carrying_none_of_its_text()
    {
        var stub = StubWith(ErrorMessage(ErrorCode.ValidationFailed, "/home/acct/link is a symlink"));

        var opened = await ClientFor(stub).ReadFileAsync("acct", "link", 100, default);

        Assert.False(opened.IsSuccess);
        Assert.Equal("AgentValidationFailed", opened.Error!.Code);
        Assert.DoesNotContain("/home", opened.Error.Code);
    }

    /// <summary>A file that grows past the ceiling while read ends the copy in a failure the caller must treat as void.</summary>
    [Fact]
    public async Task A_stream_that_ends_in_an_error_after_a_chunk_is_a_failed_copy()
    {
        var stub = StubWith(Chunk("partial"), ErrorMessage(ErrorCode.TooLarge, "grew past max_bytes"));
        var opened = await ClientFor(stub).ReadFileAsync("acct", "log.txt", 100, default);
        using var destination = new MemoryStream();

        await using var stream = opened.Value;
        var copied = await stream.CopyToAsync(destination, default);

        Assert.False(copied.IsSuccess);
        Assert.Equal("AgentTooLarge", copied.Error!.Code);
    }

    /// <summary>A connection torn mid-stream is a typed unavailable answer, not an exception.</summary>
    [Fact]
    public async Task A_connection_torn_mid_stream_is_a_typed_failure_and_not_an_exception()
    {
        var stub = StubWith(Chunk("partial"));
        stub.ReadFailure = new RpcException(new Status(StatusCode.Unavailable, "socket closed"));
        var opened = await ClientFor(stub).ReadFileAsync("acct", "log.txt", 100, default);
        using var destination = new MemoryStream();

        await using var stream = opened.Value;
        var copied = await stream.CopyToAsync(destination, default);

        Assert.False(copied.IsSuccess);
        Assert.Equal("AgentUnavailable", copied.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, copied.Error.Type);
    }

    /// <summary>An agent that cannot be reached at open is a typed unavailable answer and the call is released.</summary>
    [Fact]
    public async Task An_agent_that_cannot_be_reached_at_open_is_a_typed_failure()
    {
        var stub = StubWith();
        stub.ReadFailure = new RpcException(new Status(StatusCode.Unavailable, "no socket"));

        var opened = await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, default);

        Assert.False(opened.IsSuccess);
        Assert.Equal("AgentUnavailable", opened.Error!.Code);
        Assert.True(stub.ReadDisposed);
    }

    /// <summary>A cancellation the caller asked for is raised and not turned into a failure of the agent.</summary>
    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_raised_and_not_a_failure_of_the_agent()
    {
        using var cancellation = new CancellationTokenSource();
        var stub = StubWith(Chunk("a"), Chunk("b"));
        var opened = await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, cancellation.Token);
        using var destination = new MemoryStream();
        await cancellation.CancelAsync();

        await using var stream = opened.Value;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await stream.CopyToAsync(destination, cancellation.Token);
        });
    }

    /// <summary>Disposing a stream that was never read releases the call, which tells the agent to stop.</summary>
    [Fact]
    public async Task Disposing_a_stream_that_was_never_read_releases_the_call()
    {
        var stub = StubWith(Chunk("a"), Chunk("b"));
        var opened = await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, default);

        Assert.False(stub.ReadDisposed);

        await opened.Value.DisposeAsync();

        Assert.True(stub.ReadDisposed);
    }

    /// <summary>Only the first message is read at open, so a large file is not pulled into memory by opening it.</summary>
    [Fact]
    public async Task Opening_reads_only_the_first_message()
    {
        var stub = StubWith(Chunk("a"), Chunk("b"), Chunk("c"));

        await using var stream = (await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, default)).Value;

        Assert.Equal(1, stub.ReadMessagesHandedOut);
    }

    /// <summary>A stream is read once; a second copy is a defect of the caller and raises.</summary>
    [Fact]
    public async Task A_second_copy_of_the_same_stream_raises()
    {
        var stub = StubWith(Chunk("a"));
        await using var stream = (await ClientFor(stub).ReadFileAsync("acct", "a.txt", 100, default)).Value;
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination, default);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await stream.CopyToAsync(destination, default);
        });
    }

    /// <summary>A message carrying neither branch is an invalid response at open.</summary>
    [Fact]
    public async Task A_message_carrying_neither_branch_is_an_invalid_response_at_open()
    {
        var opened = await ClientFor(StubWith(new ReadFileResponse())).ReadFileAsync("acct", "a.txt", 100, default);

        Assert.False(opened.IsSuccess);
        Assert.Equal("AgentInvalidResponse", opened.Error!.Code);
    }

    /// <summary>Builds a stub that streams the given messages.</summary>
    /// <param name="messages">The messages, in order.</param>
    /// <returns>The stub.</returns>
    private static StubFilesService StubWith(params ReadFileResponse[] messages)
    {
        var stub = new StubFilesService();
        stub.ReadMessages.AddRange(messages);

        return stub;
    }

    /// <summary>One chunk message.</summary>
    /// <param name="text">The chunk's content, as UTF-8.</param>
    /// <returns>The message.</returns>
    private static ReadFileResponse Chunk(string text)
    {
        return new ReadFileResponse { Ok = new ReadFileChunk { Data = ByteString.CopyFromUtf8(text) } };
    }

    /// <summary>One error message.</summary>
    /// <param name="code">The wire code.</param>
    /// <param name="text">The agent's own sentence.</param>
    /// <returns>The message.</returns>
    private static ReadFileResponse ErrorMessage(ErrorCode code, string text)
    {
        return new ReadFileResponse { Error = new AgentError { Code = code, Message = text } };
    }

    /// <summary>Builds a client over the stub with a logger that discards.</summary>
    /// <param name="stub">The transport stand-in.</param>
    /// <returns>The client under test.</returns>
    private static AgentFilesClient ClientFor(StubFilesService stub)
    {
        return new AgentFilesClient(stub, NullLogger<AgentFilesClient>.Instance);
    }
}
