namespace Maran.Agent.Client.Models;

/// <summary>
/// Where the agent of one server is reached and how: a closed type with exactly two cases,
/// <see cref="UnixSocketAgentEndpoint"/> for the panel's own host and
/// <see cref="MutualTlsAgentEndpoint"/> for a managed node. Value equality is what lets the channel
/// pool tell that a server's endpoint changed and its channel must be replaced.
/// </summary>
public abstract record AgentEndpoint
{
    /// <summary>Only the two cases declared in this assembly derive from it.</summary>
    private protected AgentEndpoint()
    {
    }
}
