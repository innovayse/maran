using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Production <see cref="IBackupServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcBackupServiceInvoker : IBackupServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IBackupServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcBackupServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.BackupService.BackupServiceClient Client
    {
        get { return new Maran.Agent.V1.BackupService.BackupServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CreateBackupResponse> CreateBackupAsync(
        CreateBackupRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.CreateBackup(request, cancellationToken: cancellationToken);

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
        using var call = Client.RestoreBackup(request, cancellationToken: cancellationToken);

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
        return await Client.ListBackupsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<VerifyBackupResponse> VerifyBackupAsync(
        VerifyBackupRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.VerifyBackupAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteBackupResponse> DeleteBackupAsync(
        DeleteBackupRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteBackupAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProbeDestinationResponse> ProbeDestinationAsync(
        ProbeDestinationRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ProbeDestinationAsync(request, cancellationToken: cancellationToken);
    }
}
