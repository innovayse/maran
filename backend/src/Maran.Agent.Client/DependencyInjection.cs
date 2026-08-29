using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Agent.Client;

/// <summary>
/// Registration entry point of the agent client. Every project exposes exactly
/// one <c>Add&lt;Project&gt;</c> method here, so the Host never news up agent
/// types itself and <c>Program.cs</c> stays a table of contents.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the typed agent clients over one shared unix-socket channel.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="socketPath">
    /// Path to the agent's unix socket (configuration key <c>Agent:SocketPath</c>;
    /// production default <c>/run/maran/agent.sock</c>).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgentClient(this IServiceCollection services, string socketPath)
    {
        // The channel is its own singleton so the container owns its lifetime and disposes it on
        // shutdown; every future per-service client resolves this one channel rather than opening
        // a second connection to the same socket.
        services.AddSingleton(_ =>
        {
            return AgentChannel.CreateUnixSocket(socketPath);
        });
        services.AddSingleton<IAgentSystemClient>(
            provider =>
            {
                return new AgentSystemClient(provider.GetRequiredService<GrpcChannel>());
            });
        return services;
    }
}
