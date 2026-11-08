using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>Production <see cref="IFtpsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFtpsServiceInvoker : IFtpsServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IFtpsServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcFtpsServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.FtpsService.FtpsServiceClient Client
    {
        get { return new Maran.Agent.V1.FtpsService.FtpsServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<EnableFtpsResponse> EnableFtpsAsync(
        EnableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.EnableFtpsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableFtpsResponse> DisableFtpsAsync(
        DisableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DisableFtpsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetFtpsStatusResponse> GetFtpsStatusAsync(
        GetFtpsStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetFtpsStatusAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReloadFtpsTlsResponse> ReloadFtpsTlsAsync(
        ReloadFtpsTlsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ReloadFtpsTlsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateFtpsUserResponse> CreateFtpsUserAsync(
        CreateFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateFtpsUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetFtpsPasswordResponse> SetFtpsPasswordAsync(
        SetFtpsPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetFtpsPasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteFtpsUserResponse> DeleteFtpsUserAsync(
        DeleteFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteFtpsUserAsync(request, cancellationToken: cancellationToken);
    }
}
