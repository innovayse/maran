using Google.Protobuf;
using Maran.Agent.Client.Services.TransferService;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>
/// Mapping contract of the artifact rpcs (issue #127): a stream states exactly one ending, a refusal carries its
/// reason, and the signing key is revealed only on the wire. Each refusal sits beside the accepted shape.
/// </summary>
public sealed class BackupTransferTests
{
    /// <summary>A fragment unique enough to hunt for in anything that was printed.</summary>
    private const string KeySentinel = "SIGNING-KEY-SENTINEL-91c4";

    /// <summary>The panel's signing key as it is stored: PKCS#8 DER, base64-encoded.</summary>
    private static readonly SensitiveString SigningKey =
        new(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(KeySentinel)));

    /// <summary>Creates a client over a stub transport.</summary>
    /// <param name="stub">The stub.</param>
    /// <returns>The client.</returns>
    private static AgentTransferClient NewClient(StubTransferService stub)
    {
        return new AgentTransferClient(stub, NullLogger<AgentTransferClient>.Instance);
    }

    /// <summary>Collects every event of an export.</summary>
    /// <param name="stub">The stub.</param>
    /// <returns>The events, in order.</returns>
    private static async Task<List<AgentBackupExportEvent>> ExportAsync(StubTransferService stub)
    {
        var events = new List<AgentBackupExportEvent>();

        await foreach (var exported in NewClient(stub).ExportBackupAsync("alice", "b", SigningKey, CancellationToken.None))
        {
            events.Add(exported);
        }

        return events;
    }

    /// <summary>A header, a chunk and a done map to their cases, and the key reaches the wire request.</summary>
    [Fact]
    public async Task A_complete_export_maps_to_header_chunk_and_done()
    {
        var stub = new StubTransferService();
        stub.ExportResponses.Add(new Wire.ExportBackupResponse
        {
            Header = new Wire.ExportBackupHeader
            {
                Manifest = ByteString.CopyFromUtf8("{}"),
                Signature = ByteString.CopyFromUtf8("s"),
                PublicKey = ByteString.CopyFromUtf8("k"),
            },
        });
        stub.ExportResponses.Add(new Wire.ExportBackupResponse { Chunk = new Wire.ExportBackupChunk { Data = ByteString.CopyFromUtf8("abc") } });
        stub.ExportResponses.Add(new Wire.ExportBackupResponse { Done = new Wire.ExportBackupDone { ArtifactBytes = 3 } });

        var events = await ExportAsync(stub);

        Assert.IsType<AgentBackupExportHeader>(events[0]);
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(((AgentBackupExportChunk)events[1]).Data.Span));
        Assert.Equal(3, ((AgentBackupExportDone)events[2]).ArtifactBytes);
        Assert.Equal(KeySentinel, stub.LastExportRequest!.SigningKey.ToStringUtf8());
    }

    /// <summary>A stream that ends with no terminal message is a failure, never a quiet completion.</summary>
    [Fact]
    public async Task An_export_that_ends_without_an_ending_is_a_failure()
    {
        var stub = new StubTransferService();
        stub.ExportResponses.Add(new Wire.ExportBackupResponse { Chunk = new Wire.ExportBackupChunk { Data = ByteString.CopyFromUtf8("abc") } });

        var events = await ExportAsync(stub);

        Assert.IsType<AgentBackupExportFailed>(events[^1]);
        Assert.Equal("AgentInvalidResponse", ((AgentBackupExportFailed)events[^1]).Error.Code);
    }

    /// <summary>An empty chunk is not something the agent may send; it ends the export as a failure.</summary>
    [Fact]
    public async Task An_empty_chunk_ends_the_export_as_a_failure()
    {
        var stub = new StubTransferService();
        stub.ExportResponses.Add(new Wire.ExportBackupResponse { Chunk = new Wire.ExportBackupChunk() });

        var events = await ExportAsync(stub);

        Assert.IsType<AgentBackupExportFailed>(Assert.Single(events));
    }

    /// <summary>The refusal reason of an import travels beside the code.</summary>
    [Fact]
    public async Task A_refused_import_carries_the_reason_the_agent_gave()
    {
        var stub = new StubTransferService
        {
            ImportResponse = new Wire.ImportBackupResponse
            {
                Error = new Wire.AgentError { Code = Wire.ErrorCode.ValidationFailed, Message = "no" },
                RefusalReason = Wire.ImportRefusalReason.FingerprintMismatch,
            },
        };

        var result = await NewClient(stub).ImportBackupAsync(Header(), Chunks(), CancellationToken.None);

        var refused = Assert.IsType<AgentBackupImportRefused>(result);
        Assert.Equal(BackupImportRefusal.FingerprintMismatch, refused.Reason);
    }

    /// <summary>The inverse control: a published import maps to its figures, and the header is sent before the chunks.</summary>
    [Fact]
    public async Task A_published_import_maps_and_the_header_is_sent_first()
    {
        var stub = new StubTransferService
        {
            ImportResponse = new Wire.ImportBackupResponse
            {
                Ok = new Wire.ImportBackupOk { BackupId = "id", ArtifactBytes = 5, ArtifactSha256 = new string('a', 64), DatabaseCount = 2 },
            },
        };

        var result = await NewClient(stub).ImportBackupAsync(Header(), Chunks(), CancellationToken.None);

        var imported = Assert.IsType<AgentBackupImported>(result);
        Assert.Equal(2, imported.DatabaseCount);
        Assert.Equal(Wire.ImportBackupRequest.PartOneofCase.Header, stub.ImportRequests[0].PartCase);
        Assert.Equal(Wire.ImportBackupRequest.PartOneofCase.Chunk, stub.ImportRequests[1].PartCase);
        Assert.Equal(2, stub.ImportRequests.Count);
    }

    /// <summary>A response with neither branch is refused, not believed.</summary>
    [Fact]
    public async Task An_import_response_with_no_branch_is_refused()
    {
        var result = await NewClient(new StubTransferService()).ImportBackupAsync(Header(), Chunks(), CancellationToken.None);

        Assert.IsType<AgentBackupImportRefused>(result);
    }

    /// <summary>A header for an import.</summary>
    /// <returns>The header.</returns>
    private static AgentBackupImportHeader Header()
    {
        return new AgentBackupImportHeader([1], [2], [3], new string('f', 64), 0);
    }

    /// <summary>One chunk of an artifact, and an empty one the mapper must drop.</summary>
    /// <returns>The chunks.</returns>
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Chunks()
    {
        await Task.Yield();

        yield return new byte[] { 1, 2, 3 };
        yield return ReadOnlyMemory<byte>.Empty;
    }
}
