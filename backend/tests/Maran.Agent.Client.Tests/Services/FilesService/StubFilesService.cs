using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.FilesService;

/// <summary>Stub of <c>IFilesServiceInvoker</c> returning canned responses and recording requests.</summary>
internal sealed class StubFilesService : IFilesServiceInvoker
{
    /// <summary>Response returned from <see cref="WriteFileAsync"/>.</summary>
    public WriteFileResponse WriteResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="DeleteEntryAsync"/>.</summary>
    public DeleteEntryResponse DeleteResponse { get; set; } = new();

    /// <summary>The last write request, for asserting the header and the payload.</summary>
    public WriteFileRequest? LastWriteRequest { get; private set; }

    /// <summary>The last delete request.</summary>
    public DeleteEntryRequest? LastDeleteRequest { get; private set; }

    /// <summary>The header of the last streamed write.</summary>
    public WriteFileHeader? LastStreamHeader { get; private set; }

    /// <summary>The bytes the last streamed write carried.</summary>
    public byte[]? LastStreamBody { get; private set; }

    /// <summary>What a streamed write throws instead of answering, or null to answer <see cref="WriteResponse"/>.</summary>
    public Exception? StreamFailure { get; set; }

    /// <inheritdoc/>
    public Task<WriteFileResponse> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken)
    {
        LastWriteRequest = request;
        return Task.FromResult(WriteResponse);
    }

    /// <inheritdoc/>
    public async Task<WriteFileResponse> WriteFileStreamAsync(
        WriteFileHeader header,
        Stream body,
        CancellationToken cancellationToken)
    {
        LastStreamHeader = header;
        using var received = new MemoryStream();
        await body.CopyToAsync(received, cancellationToken);
        LastStreamBody = received.ToArray();

        if (StreamFailure is not null)
        {
            throw StreamFailure;
        }

        return WriteResponse;
    }

    /// <inheritdoc/>
    public Task<DeleteEntryResponse> DeleteEntryAsync(DeleteEntryRequest request, CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }

    /// <summary>Response returned from <see cref="ListDirectoryAsync"/>.</summary>
    public ListDirectoryResponse ListResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="SearchEntriesAsync"/>.</summary>
    public SearchEntriesResponse SearchResponse { get; set; } = new();

    /// <summary>The messages <see cref="ReadFileAsync"/> streams, in order.</summary>
    public List<ReadFileResponse> ReadMessages { get; } = [];

    /// <summary>What the read stream throws once <see cref="ReadMessages"/> is exhausted, or null to end normally.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>The last list request.</summary>
    public ListDirectoryRequest? LastListRequest { get; private set; }

    /// <summary>The last search request.</summary>
    public SearchEntriesRequest? LastSearchRequest { get; private set; }

    /// <summary>The last read request.</summary>
    public ReadFileRequest? LastReadRequest { get; private set; }

    /// <summary>Whether the read stream has been disposed, which is what releases the gRPC call.</summary>
    public bool ReadDisposed { get; private set; }

    /// <summary>How many messages the read stream has handed out.</summary>
    public int ReadMessagesHandedOut { get; private set; }

    /// <inheritdoc/>
    public Task<ListDirectoryResponse> ListDirectoryAsync(
        ListDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        LastListRequest = request;
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public Task<SearchEntriesResponse> SearchEntriesAsync(
        SearchEntriesRequest request,
        CancellationToken cancellationToken)
    {
        LastSearchRequest = request;
        return Task.FromResult(SearchResponse);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ReadFileResponse> ReadFileAsync(
        ReadFileRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastReadRequest = request;

        try
        {
            foreach (var message in ReadMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadMessagesHandedOut++;
                yield return message;
                await Task.Yield();
            }

            if (ReadFailure is not null)
            {
                throw ReadFailure;
            }
        }
        finally
        {
            ReadDisposed = true;
        }
    }

    /// <summary>Response returned from <see cref="CreateDirectoryAsync"/>.</summary>
    public CreateDirectoryResponse CreateDirectoryResponse { get; set; } = new();

    /// <summary>The last <c>CreateDirectory</c> request.</summary>
    public CreateDirectoryRequest? LastCreateDirectoryRequest { get; private set; }

    /// <summary>Response returned from <see cref="MoveEntryAsync"/>.</summary>
    public MoveEntryResponse MoveEntryResponse { get; set; } = new();

    /// <summary>The last <c>MoveEntry</c> request.</summary>
    public MoveEntryRequest? LastMoveEntryRequest { get; private set; }

    /// <summary>Response returned from <see cref="CopyEntryAsync"/>.</summary>
    public CopyEntryResponse CopyEntryResponse { get; set; } = new();

    /// <summary>The last <c>CopyEntry</c> request.</summary>
    public CopyEntryRequest? LastCopyEntryRequest { get; private set; }

    /// <summary>Response returned from <see cref="ChangePermissionsAsync"/>.</summary>
    public ChangePermissionsResponse ChangePermissionsResponse { get; set; } = new();

    /// <summary>The last <c>ChangePermissions</c> request.</summary>
    public ChangePermissionsRequest? LastChangePermissionsRequest { get; private set; }

    /// <summary>Response returned from <see cref="TrashEntryAsync"/>.</summary>
    public TrashEntryResponse TrashEntryResponse { get; set; } = new();

    /// <summary>The last <c>TrashEntry</c> request.</summary>
    public TrashEntryRequest? LastTrashEntryRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListTrashAsync"/>.</summary>
    public ListTrashResponse ListTrashResponse { get; set; } = new();

    /// <summary>The last <c>ListTrash</c> request.</summary>
    public ListTrashRequest? LastListTrashRequest { get; private set; }

    /// <summary>Response returned from <see cref="RestoreTrashEntryAsync"/>.</summary>
    public RestoreTrashEntryResponse RestoreTrashEntryResponse { get; set; } = new();

    /// <summary>The last <c>RestoreTrashEntry</c> request.</summary>
    public RestoreTrashEntryRequest? LastRestoreTrashEntryRequest { get; private set; }

    /// <summary>Response returned from <see cref="EmptyTrashAsync"/>.</summary>
    public EmptyTrashResponse EmptyTrashResponse { get; set; } = new();

    /// <summary>The last <c>EmptyTrash</c> request.</summary>
    public EmptyTrashRequest? LastEmptyTrashRequest { get; private set; }

    /// <summary>Response returned from <see cref="PurgeExpiredTrashAsync"/>.</summary>
    public PurgeExpiredTrashResponse PurgeExpiredTrashResponse { get; set; } = new();

    /// <summary>The last <c>PurgeExpiredTrash</c> request.</summary>
    public PurgeExpiredTrashRequest? LastPurgeExpiredTrashRequest { get; private set; }

    /// <summary>Response returned from <see cref="ForceDeleteEntryAsync"/>.</summary>
    public ForceDeleteEntryResponse ForceDeleteEntryResponse { get; set; } = new();

    /// <summary>The last <c>ForceDeleteEntry</c> request.</summary>
    public ForceDeleteEntryRequest? LastForceDeleteEntryRequest { get; private set; }

    /// <summary>Response returned from <see cref="CreateArchiveAsync"/>.</summary>
    public CreateArchiveResponse CreateArchiveResponse { get; set; } = new();

    /// <summary>The last <c>CreateArchive</c> request.</summary>
    public CreateArchiveRequest? LastCreateArchiveRequest { get; private set; }

    /// <summary>Response returned from <see cref="ExtractArchiveAsync"/>.</summary>
    public ExtractArchiveResponse ExtractArchiveResponse { get; set; } = new();

    /// <summary>The last <c>ExtractArchive</c> request.</summary>
    public ExtractArchiveRequest? LastExtractArchiveRequest { get; private set; }

    /// <inheritdoc/>
    public Task<CreateDirectoryResponse> CreateDirectoryAsync(CreateDirectoryRequest request, CancellationToken cancellationToken)
    {
        LastCreateDirectoryRequest = request;
        return Task.FromResult(CreateDirectoryResponse);
    }

    /// <inheritdoc/>
    public Task<MoveEntryResponse> MoveEntryAsync(MoveEntryRequest request, CancellationToken cancellationToken)
    {
        LastMoveEntryRequest = request;
        return Task.FromResult(MoveEntryResponse);
    }

    /// <inheritdoc/>
    public Task<CopyEntryResponse> CopyEntryAsync(CopyEntryRequest request, CancellationToken cancellationToken)
    {
        LastCopyEntryRequest = request;
        return Task.FromResult(CopyEntryResponse);
    }

    /// <inheritdoc/>
    public Task<ChangePermissionsResponse> ChangePermissionsAsync(ChangePermissionsRequest request, CancellationToken cancellationToken)
    {
        LastChangePermissionsRequest = request;
        return Task.FromResult(ChangePermissionsResponse);
    }

    /// <inheritdoc/>
    public Task<TrashEntryResponse> TrashEntryAsync(TrashEntryRequest request, CancellationToken cancellationToken)
    {
        LastTrashEntryRequest = request;
        return Task.FromResult(TrashEntryResponse);
    }

    /// <inheritdoc/>
    public Task<ListTrashResponse> ListTrashAsync(ListTrashRequest request, CancellationToken cancellationToken)
    {
        LastListTrashRequest = request;
        return Task.FromResult(ListTrashResponse);
    }

    /// <inheritdoc/>
    public Task<RestoreTrashEntryResponse> RestoreTrashEntryAsync(RestoreTrashEntryRequest request, CancellationToken cancellationToken)
    {
        LastRestoreTrashEntryRequest = request;
        return Task.FromResult(RestoreTrashEntryResponse);
    }

    /// <inheritdoc/>
    public Task<EmptyTrashResponse> EmptyTrashAsync(EmptyTrashRequest request, CancellationToken cancellationToken)
    {
        LastEmptyTrashRequest = request;
        return Task.FromResult(EmptyTrashResponse);
    }

    /// <inheritdoc/>
    public Task<PurgeExpiredTrashResponse> PurgeExpiredTrashAsync(PurgeExpiredTrashRequest request, CancellationToken cancellationToken)
    {
        LastPurgeExpiredTrashRequest = request;
        return Task.FromResult(PurgeExpiredTrashResponse);
    }

    /// <inheritdoc/>
    public Task<ForceDeleteEntryResponse> ForceDeleteEntryAsync(ForceDeleteEntryRequest request, CancellationToken cancellationToken)
    {
        LastForceDeleteEntryRequest = request;
        return Task.FromResult(ForceDeleteEntryResponse);
    }

    /// <inheritdoc/>
    public Task<CreateArchiveResponse> CreateArchiveAsync(CreateArchiveRequest request, CancellationToken cancellationToken)
    {
        LastCreateArchiveRequest = request;
        return Task.FromResult(CreateArchiveResponse);
    }

    /// <inheritdoc/>
    public Task<ExtractArchiveResponse> ExtractArchiveAsync(ExtractArchiveRequest request, CancellationToken cancellationToken)
    {
        LastExtractArchiveRequest = request;
        return Task.FromResult(ExtractArchiveResponse);
    }
}
