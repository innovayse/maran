using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>An <see cref="IAgentChannels"/> that declines every server, as the pool does for one it will not dial.</summary>
internal sealed class RefusingAgentChannels : IAgentChannels
{
    /// <summary>The refusal every ask raises.</summary>
    private readonly Error _refusal;

    /// <summary>How many times a channel has been asked for.</summary>
    public int Requests { get; private set; }

    /// <summary>Creates the channels refusing with <paramref name="refusal"/>.</summary>
    /// <param name="refusal">The typed failure the exception carries.</param>
    public RefusingAgentChannels(Error refusal)
    {
        _refusal = refusal;
    }

    /// <inheritdoc/>
    public GrpcChannel ForCurrent()
    {
        Requests++;
        throw new AgentEndpointRefusedException(_refusal);
    }

    /// <inheritdoc/>
    public GrpcChannel ForServer(Guid serverId)
    {
        Requests++;
        throw new AgentEndpointRefusedException(_refusal);
    }
}
