using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Production <see cref="ISystemServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSystemServiceInvoker : ISystemServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ISystemServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcSystemServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.SystemService.SystemServiceClient Client
    {
        get { return new Maran.Agent.V1.SystemService.SystemServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<GetAgentInfoResponse> GetAgentInfoAsync(CancellationToken ct)
    {
        return await Client.GetAgentInfoAsync(new GetAgentInfoRequest(), cancellationToken: ct);
    }
}
