using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SftpService;

/// <summary>Production <see cref="ISftpServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSftpServiceInvoker : ISftpServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ISftpServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcSftpServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.SftpService.SftpServiceClient Client
    {
        get { return new Maran.Agent.V1.SftpService.SftpServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<CreateSftpUserResponse> CreateSftpUserAsync(
        CreateSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateSftpUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetSftpPasswordResponse> SetSftpPasswordAsync(
        SetSftpPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetSftpPasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteSftpUserResponse> DeleteSftpUserAsync(
        DeleteSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteSftpUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountLoginsLockedResponse> SetAccountLoginsLockedAsync(
        SetAccountLoginsLockedRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountLoginsLockedAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EndSftpSessionsResponse> EndSftpSessionsAsync(
        EndSftpSessionsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.EndSftpSessionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetSftpAuthorizedKeysResponse> SetSftpAuthorizedKeysAsync(
        SetSftpAuthorizedKeysRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetSftpAuthorizedKeysAsync(request, cancellationToken: cancellationToken);
    }
}
