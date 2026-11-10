using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Production <see cref="ITransferServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcTransferServiceInvoker : ITransferServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ITransferServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcTransferServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.TransferService.TransferServiceClient Client
    {
        get { return new Maran.Agent.V1.TransferService.TransferServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<GenerateSourceKeyResponse> GenerateSourceKeyAsync(
        GenerateSourceKeyRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GenerateSourceKeyAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ScanHostKeyResponse> ScanHostKeyAsync(
        ScanHostKeyRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ScanHostKeyAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TestSourceResponse> TestSourceAsync(
        TestSourceRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.TestSourceAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ReadSourceInventoryResponse> ReadSourceInventoryAsync(
        ReadSourceInventoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.ReadSourceInventory(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ExportBackupResponse> ExportBackupAsync(
        ExportBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.ExportBackup(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async Task<ImportBackupResponse> ImportBackupAsync(
        IAsyncEnumerable<ImportBackupRequest> requests,
        CancellationToken cancellationToken)
    {
        using var call = Client.ImportBackup(cancellationToken: cancellationToken);

        await foreach (var request in requests.WithCancellation(cancellationToken))
        {
            await call.RequestStream.WriteAsync(request, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();

        return await call.ResponseAsync;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PullAccountFilesResponse> PullAccountFilesAsync(
        PullAccountFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.PullAccountFiles(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamRemoteDatabaseResponse> StreamRemoteDatabaseAsync(
        StreamRemoteDatabaseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.StreamRemoteDatabase(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }
}
