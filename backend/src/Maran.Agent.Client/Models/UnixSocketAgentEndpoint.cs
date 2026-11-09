namespace Maran.Agent.Client.Models;

/// <summary>The agent on the panel's own host, reached over its unix domain socket.</summary>
/// <param name="SocketPath">Filesystem path of the agent's unix socket.</param>
public sealed record UnixSocketAgentEndpoint(string SocketPath) : AgentEndpoint;
