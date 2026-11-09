using Maran.Agent.Client.Models;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Answers where the agent of a server is reached. The registry of servers implements it; a panel
/// without one is served by <see cref="Channels.LocalOnlyEndpointSource"/>.
/// </summary>
/// <remarks>
/// The answer is synchronous on purpose: it is asked on the path of every agent call, so an
/// implementation answers from memory and refreshes that memory elsewhere. A change of endpoint is
/// noticed by the next call, which receives an endpoint that no longer equals the cached one.
/// </remarks>
public interface IAgentEndpointSource
{
    /// <summary>Resolves the endpoint of one server.</summary>
    /// <param name="serverId">The server whose agent is wanted.</param>
    /// <returns>
    /// The endpoint, or the refusal: <c>AgentServerUnknown</c>, <c>AgentServerNotApproved</c> or
    /// <c>AgentServerDisabled</c>. A server that is not approved is never dialled.
    /// </returns>
    Result<AgentEndpoint> Resolve(Guid serverId);
}
