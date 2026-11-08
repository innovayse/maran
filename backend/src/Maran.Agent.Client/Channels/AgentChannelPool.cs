using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Models;
using Maran.SharedKernel.Interfaces;

namespace Maran.Agent.Client.Channels;

/// <summary>
/// One <see cref="GrpcChannel"/> per server, created when first needed from what
/// <see cref="IAgentEndpointSource"/> says and replaced when that answer changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pool and not a channel per call.</b> A channel owns the HTTP/2 connection, and for a node
/// the TLS session, so it is shared by every client of the process the way the single unix-socket
/// channel always was. The local server's channel is built by the same factory as before, so the
/// default behaves as it did.
/// </para>
/// <para>
/// <b>The source is asked on every lookup</b> and the cached channel is kept only while the answer
/// still equals the endpoint it was built for. That is the whole change-notification mechanism: a
/// re-approved node (new pin), a replaced panel identity or a changed address yields an endpoint that
/// no longer equals the cached one, and the next call builds a new channel. The replaced channel is
/// disposed at once; a call still running on it fails as unavailable, which is the right answer for a
/// node whose identity was just changed. A server that stops resolving (removed, disabled) has its
/// channel disposed on the lookup that discovers it, so no connection outlives the permission to use it.
/// </para>
/// </remarks>
internal sealed class AgentChannelPool : IAgentChannels, IDisposable
{
    /// <summary>Where each server's agent is.</summary>
    private readonly IAgentEndpointSource _source;

    /// <summary>Which server the current flow of execution addresses.</summary>
    private readonly IAgentTarget _target;

    /// <summary>The clock a node channel judges certificate validity by.</summary>
    private readonly IClock _clock;

    /// <summary>Guards <see cref="_channels"/> and <see cref="_disposed"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The live channel of each server with the endpoint it was built for.</summary>
    private readonly Dictionary<Guid, (AgentEndpoint Endpoint, GrpcChannel Channel)> _channels = [];

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    private bool _disposed;

    /// <summary>Creates the pool.</summary>
    /// <param name="source">Resolves a server id to its endpoint.</param>
    /// <param name="target">Names the server the current flow of execution addresses.</param>
    /// <param name="clock">The panel's clock.</param>
    public AgentChannelPool(IAgentEndpointSource source, IAgentTarget target, IClock clock)
    {
        _source = source;
        _target = target;
        _clock = clock;
    }

    /// <inheritdoc/>
    public GrpcChannel ForCurrent()
    {
        return ForServer(_target.Current);
    }

    /// <inheritdoc/>
    public GrpcChannel ForServer(Guid serverId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var resolved = _source.Resolve(serverId);
            if (!resolved.IsSuccess)
            {
                if (_channels.Remove(serverId, out var stale))
                {
                    stale.Channel.Dispose();
                }

                throw new AgentEndpointRefusedException(resolved.Error!);
            }

            var endpoint = resolved.Value;
            if (_channels.TryGetValue(serverId, out var current))
            {
                if (current.Endpoint.Equals(endpoint))
                {
                    return current.Channel;
                }

                current.Channel.Dispose();
            }

            var channel = Create(endpoint);
            _channels[serverId] = (endpoint, channel);
            return channel;
        }
    }

    /// <summary>Disposes every channel; further lookups fail.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _channels.Values)
            {
                entry.Channel.Dispose();
            }

            _channels.Clear();
        }
    }

    /// <summary>Builds the channel an endpoint calls for.</summary>
    /// <param name="endpoint">The resolved endpoint.</param>
    /// <returns>A unix-socket channel or a mutual-TLS channel.</returns>
    private GrpcChannel Create(AgentEndpoint endpoint)
    {
        return endpoint switch
        {
            UnixSocketAgentEndpoint unix => AgentChannel.CreateUnixSocket(unix.SocketPath),
            MutualTlsAgentEndpoint tls => AgentChannel.CreateMutualTls(tls, _clock),
            _ => throw new InvalidOperationException("Unknown agent endpoint case."),
        };
    }
}
