using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Constants;

namespace Maran.Agent.Client.Targets;

/// <summary>
/// <see cref="IAgentTarget"/> kept in an <see cref="AsyncLocal{T}"/>, so concurrent requests and jobs
/// each address their own server through the one singleton.
/// </summary>
internal sealed class AsyncLocalAgentTarget : IAgentTarget
{
    /// <summary>The server the current flow of execution addresses; <c>null</c> when no scope is open.</summary>
    private readonly AsyncLocal<Guid?> _slot = new();

    /// <inheritdoc/>
    public Guid Current
    {
        get { return _slot.Value ?? FleetServers.LocalId; }
    }

    /// <inheritdoc/>
    public IDisposable Begin(Guid serverId)
    {
        if (serverId == Guid.Empty)
        {
            throw new ArgumentException("A server id must not be empty.", nameof(serverId));
        }

        var scope = new AgentTargetScope(_slot, _slot.Value);
        _slot.Value = serverId;
        return scope;
    }
}
