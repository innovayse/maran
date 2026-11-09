using Grpc.Net.Client;
using Maran.Agent.Client.Interfaces;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// An <see cref="IAgentChannels"/> that picks the channel by an ambient (async-flowing) selection and
/// records the selection it saw on every ask, standing in for the routing pool and the agent target.
/// </summary>
/// <remarks>
/// Ambient on purpose: the production target is ambient too, and what a decorator has to preserve
/// across its retries and its awaits is exactly that flow. A selection passed as an argument would
/// not test it.
/// </remarks>
internal sealed class AmbientSelectionAgentChannels : IAgentChannels
{
    /// <summary>The selection of the logical call currently running; flows with the async context.</summary>
    private static readonly AsyncLocal<string?> Selected = new();

    /// <summary>The channel each selection name stands for.</summary>
    private readonly Dictionary<string, GrpcChannel> _channels;

    /// <summary>Every selection seen by <see cref="ForCurrent"/>, in the order of asking.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _seen = new();

    /// <summary>Every selection seen so far, in the order of asking.</summary>
    public IReadOnlyCollection<string> Seen
    {
        get { return _seen.ToArray(); }
    }

    /// <summary>Creates the channels over the named channels.</summary>
    /// <param name="channels">The channel each selection name stands for.</param>
    public AmbientSelectionAgentChannels(Dictionary<string, GrpcChannel> channels)
    {
        _channels = channels;
    }

    /// <summary>Aims the logical call that is about to start at <paramref name="name"/>.</summary>
    /// <param name="name">A key of the channels this instance was built with.</param>
    public static void Select(string name)
    {
        Selected.Value = name;
    }

    /// <inheritdoc/>
    public GrpcChannel ForCurrent()
    {
        var name = Selected.Value ?? throw new InvalidOperationException("No server is selected for this call.");
        _seen.Enqueue(name);
        return _channels[name];
    }

    /// <inheritdoc/>
    public GrpcChannel ForServer(Guid serverId)
    {
        return ForCurrent();
    }
}
