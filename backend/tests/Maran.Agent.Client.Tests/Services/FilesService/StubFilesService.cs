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

    /// <inheritdoc/>
    public Task<WriteFileResponse> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken)
    {
        LastWriteRequest = request;
        return Task.FromResult(WriteResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteEntryResponse> DeleteEntryAsync(DeleteEntryRequest request, CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }
}
