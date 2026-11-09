using System.Runtime.CompilerServices;
using Google.Protobuf;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>Production <see cref="IFilesServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFilesServiceInvoker : IFilesServiceInvoker
{
    /// <summary>How many body bytes travel in one message of an upload stream.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IFilesServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcFilesServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.FilesService.FilesServiceClient Client
    {
        get { return new Maran.Agent.V1.FilesService.FilesServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<WriteFileResponse> WriteFileAsync(
        WriteFileRequest request,
        CancellationToken cancellationToken)
    {
        using var call = Client.WriteFile(cancellationToken: cancellationToken);

        try
        {
            await call.RequestStream.WriteAsync(request, cancellationToken);

            // The agent finishes the write when the request stream closes; without this the call hangs
            // until the deadline and the file is never written.
            await call.RequestStream.CompleteAsync();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The stream writer reports a connection that could not be made, or one that was torn,
            // as a cancellation although nobody cancelled (the channel sets
            // ThrowOperationCanceledOnCancellation). The call's own response task holds the real
            // status, so awaiting it raises the RpcException every unary call raises and the
            // pipeline maps it to the same typed answer (issue #87).
            return await call.ResponseAsync;
        }

        return await call.ResponseAsync;
    }

    /// <inheritdoc/>
    public async Task<WriteFileResponse> WriteFileStreamAsync(
        WriteFileHeader header,
        Stream body,
        CancellationToken cancellationToken)
    {
        using var call = Client.WriteFile(cancellationToken: cancellationToken);

        try
        {
            await call.RequestStream.WriteAsync(new WriteFileRequest { Header = header }, cancellationToken);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await call.RequestStream.WriteAsync(
                    new WriteFileRequest { Chunk = ByteString.CopyFrom(buffer, 0, read) },
                    cancellationToken);
            }

            await call.RequestStream.CompleteAsync();
        }
        catch (Exception exception) when (
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || exception is InvalidOperationException)
        {
            // Either the stream writer reported a connection that could not be made as a cancellation
            // nobody asked for (see WriteFileAsync), or the agent already ended the call — refusing a
            // body past its cap does exactly that — so a further write is refused locally. The call's
            // own response holds the real answer or status in both cases.
            return await call.ResponseAsync;
        }

        return await call.ResponseAsync;
    }

    /// <inheritdoc/>
    public async Task<DeleteEntryResponse> DeleteEntryAsync(
        DeleteEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListDirectoryResponse> ListDirectoryAsync(
        ListDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListDirectoryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SearchEntriesResponse> SearchEntriesAsync(
        SearchEntriesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SearchEntriesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ReadFileResponse> ReadFileAsync(
        ReadFileRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Disposed when the caller disposes the enumerator, which is how a download the browser
        // abandoned stops the agent reading the file: the call is cancelled, not left to run out.
        using var call = Client.ReadFile(request, cancellationToken: cancellationToken);

        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            yield return call.ResponseStream.Current;
        }
    }

    /// <inheritdoc/>
    public async Task<CreateDirectoryResponse> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateDirectoryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MoveEntryResponse> MoveEntryAsync(
        MoveEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.MoveEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CopyEntryResponse> CopyEntryAsync(
        CopyEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CopyEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ChangePermissionsResponse> ChangePermissionsAsync(
        ChangePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ChangePermissionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TrashEntryResponse> TrashEntryAsync(
        TrashEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.TrashEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListTrashResponse> ListTrashAsync(
        ListTrashRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListTrashAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RestoreTrashEntryResponse> RestoreTrashEntryAsync(
        RestoreTrashEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RestoreTrashEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EmptyTrashResponse> EmptyTrashAsync(
        EmptyTrashRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.EmptyTrashAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PurgeExpiredTrashResponse> PurgeExpiredTrashAsync(
        PurgeExpiredTrashRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.PurgeExpiredTrashAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ForceDeleteEntryResponse> ForceDeleteEntryAsync(
        ForceDeleteEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ForceDeleteEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateArchiveResponse> CreateArchiveAsync(
        CreateArchiveRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateArchiveAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ExtractArchiveResponse> ExtractArchiveAsync(
        ExtractArchiveRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ExtractArchiveAsync(request, cancellationToken: cancellationToken);
    }
}
