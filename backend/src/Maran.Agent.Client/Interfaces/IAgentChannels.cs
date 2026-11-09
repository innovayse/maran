using Grpc.Net.Client;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The gRPC channel to the agent of a server: one per server, created on first use and shared by
/// every client of this process.
/// </summary>
public interface IAgentChannels
{
    /// <summary>The channel to the server <see cref="IAgentTarget.Current"/> names.</summary>
    /// <returns>The shared channel. The caller never disposes it.</returns>
    /// <exception cref="Errors.AgentEndpointRefusedException">
    /// The server is unknown, not approved or disabled. The client methods catch it and answer with
    /// its <see cref="Errors.AgentEndpointRefusedException.Error"/> as a failed result.
    /// </exception>
    GrpcChannel ForCurrent();

    /// <summary>The channel to one named server, for work that visits servers without opening a scope.</summary>
    /// <param name="serverId">The server whose agent is wanted.</param>
    /// <returns>The shared channel. The caller never disposes it.</returns>
    /// <exception cref="Errors.AgentEndpointRefusedException">The server is unknown, not approved or disabled.</exception>
    GrpcChannel ForServer(Guid serverId);
}
