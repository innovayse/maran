using System.Text;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FilesService;

/// <summary>Mapping contract of AgentFilesClient (proto oneof → Result), and the bytes it sends.</summary>
public sealed class AgentFilesClientTests
{
    /// <summary>An ok write maps to the byte count the agent reported.</summary>
    [Fact]
    public async Task An_ok_write_maps_to_the_byte_count_the_agent_reported()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse { Ok = new WriteFileOk { BytesWritten = 42 } },
        };

        var result = await ClientFor(stub).WriteFileAsync(
            "acct", "sites/example.com/.well-known/acme-challenge/tok", "body", 0b110_100_100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42ul, result.Value);
    }

    /// <summary>A write sends the account the path and the mode in the header.</summary>
    [Fact]
    public async Task A_write_sends_the_account_the_path_and_the_mode_in_the_header()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse { Ok = new WriteFileOk { BytesWritten = 4 } },
        };

        await ClientFor(stub).WriteFileAsync(
            "acct", "sites/example.com/.well-known/acme-challenge/tok", "body", 0b110_100_100, CancellationToken.None);

        var request = Assert.IsType<WriteFileRequest>(stub.LastWriteRequest);
        Assert.Equal("acct", request.Header.AccountUsername);
        Assert.Equal("sites/example.com/.well-known/acme-challenge/tok", request.Header.Path);
        Assert.Equal(0b110_100_100u, request.Header.Mode);
    }

    /// <summary>The content is sent as utf eight with no byte order mark.</summary>
    [Fact]
    public async Task The_content_is_sent_as_utf_eight_with_no_byte_order_mark()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse { Ok = new WriteFileOk { BytesWritten = 4 } },
        };

        await ClientFor(stub).WriteFileAsync("acct", "p", "body", 0, CancellationToken.None);

        // A certificate authority fetches this file and compares it byte for byte; three bytes of
        // byte-order mark would fail every validation with no useful diagnostic.
        var sent = stub.LastWriteRequest!.Chunk.ToByteArray();
        Assert.Equal(Encoding.UTF8.GetBytes("body"), sent);
    }

    /// <summary>A refused write maps to a failed result carrying only a code.</summary>
    [Fact]
    public async Task A_refused_write_maps_to_a_failed_result_carrying_only_a_code()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.ValidationFailed,
                    Message = "path escapes /home/acct",
                },
            },
        };

        var result = await ClientFor(stub).WriteFileAsync("acct", "p", "body", 0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.DoesNotContain("/home/acct", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>A write response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_write_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await ClientFor(new StubFilesService())
            .WriteFileAsync("acct", "p", "body", 0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>An ok delete maps to success and sends the path it was given.</summary>
    [Fact]
    public async Task An_ok_delete_maps_to_success_and_sends_the_path_it_was_given()
    {
        var stub = new StubFilesService
        {
            DeleteResponse = new DeleteEntryResponse { Ok = new DeleteEntryOk() },
        };

        var result = await ClientFor(stub).DeleteEntryAsync("acct", "p", recursive: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("p", stub.LastDeleteRequest!.Path);
        Assert.False(stub.LastDeleteRequest.Recursive);
    }

    /// <summary>A delete response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_delete_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await ClientFor(new StubFilesService())
            .DeleteEntryAsync("acct", "p", recursive: false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds the production client over a transport stub.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentFilesClient ClientFor(StubFilesService stub)
    {
        return new AgentFilesClient(stub, NullLogger<AgentFilesClient>.Instance);
    }
}
