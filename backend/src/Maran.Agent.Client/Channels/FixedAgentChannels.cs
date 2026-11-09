using Grpc.Net.Client;
using Maran.Agent.Client.Interfaces;

namespace Maran.Agent.Client.Channels;

/// <summary>
/// An <see cref="IAgentChannels"/> that answers every call with the one channel it was given, whatever
/// the current agent target is.
/// </summary>
/// <remarks>
/// For a caller that has exactly one agent to talk to by construction and chose that channel itself —
/// a handshake probe over a socket path, a test against a double. It does not own the channel: the
/// code that created it disposes it, as before routing existed. The panel's own composition does not
/// use this type; it registers the routed channels, where the target decides.
/// </remarks>
internal sealed class FixedAgentChannels : IAgentChannels
{
    /// <summary>The channel every call is sent over.</summary>
    private readonly GrpcChannel _channel;

    /// <summary>Wraps <paramref name="channel"/> so a client built for routed channels can use it.</summary>
    /// <param name="channel">The channel to hand out; its owner disposes it.</param>
    public FixedAgentChannels(GrpcChannel channel)
    {
        _channel = channel;
    }

    /// <inheritdoc/>
    public GrpcChannel ForCurrent()
    {
        return _channel;
    }

    /// <inheritdoc/>
    public GrpcChannel ForServer(Guid serverId)
    {
        return _channel;
    }
}
