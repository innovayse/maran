using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Production <see cref="IBackupServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcBackupServiceInvoker : IBackupServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.BackupService.BackupServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IBackupServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcBackupServiceInvoker(Maran.Agent.V1.BackupService.BackupServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CreateBackupResponse> CreateBackupAsync(
        CreateBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.CreateBackup(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RestoreBackupResponse> RestoreBackupAsync(
        RestoreBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.RestoreBackup(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async Task<ListBackupsResponse> ListBackupsAsync(
        ListBackupsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListBackupsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteBackupResponse> DeleteBackupAsync(
        DeleteBackupRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteBackupAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProbeDestinationResponse> ProbeDestinationAsync(
        ProbeDestinationRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ProbeDestinationAsync(request, cancellationToken: cancellationToken);
    }
}
