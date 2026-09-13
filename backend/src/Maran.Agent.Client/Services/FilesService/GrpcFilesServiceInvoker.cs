using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>Production <see cref="IFilesServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFilesServiceInvoker : IFilesServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.FilesService.FilesServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IFilesServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcFilesServiceInvoker(Maran.Agent.V1.FilesService.FilesServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<WriteFileResponse> WriteFileAsync(
        WriteFileRequest request,
        CancellationToken cancellationToken)
    {
        using var call = _client.WriteFile(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(request, cancellationToken);

        // The agent finishes the write when the request stream closes; without this the call hangs
        // until the deadline and the file is never written.
        await call.RequestStream.CompleteAsync();

        return await call.ResponseAsync;
    }

    /// <inheritdoc/>
    public async Task<DeleteEntryResponse> DeleteEntryAsync(
        DeleteEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteEntryAsync(request, cancellationToken: cancellationToken);
    }
}
