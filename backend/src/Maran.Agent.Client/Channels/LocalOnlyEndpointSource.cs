using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Models;
using Maran.Agent.Client.Resources;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Channels;

/// <summary>
/// The endpoint source of a panel that knows no managed servers: the local server resolves to the
/// agent's unix socket and every other id is unknown. It is what <c>AddAgentRouting</c> registers
/// unless a registry of servers replaces it, so a panel without that registry works exactly as it
/// did before servers could be added.
/// </summary>
internal sealed class LocalOnlyEndpointSource : IAgentEndpointSource
{
    /// <summary>The one endpoint this source knows.</summary>
    private readonly UnixSocketAgentEndpoint _local;

    /// <summary>Creates the source over the local agent's socket.</summary>
    /// <param name="socketPath">Path to the agent's unix socket (configuration key <c>Agent:SocketPath</c>).</param>
    public LocalOnlyEndpointSource(string socketPath)
    {
        _local = new UnixSocketAgentEndpoint(socketPath);
    }

    /// <inheritdoc/>
    public Result<AgentEndpoint> Resolve(Guid serverId)
    {
        if (serverId == FleetServers.LocalId)
        {
            return Result<AgentEndpoint>.Ok(_local);
        }

        return Result<AgentEndpoint>.Fail(
            Error.Of(nameof(ErrorMessages.AgentServerUnknown), ErrorType.NotFound));
    }
}
