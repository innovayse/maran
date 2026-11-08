using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Agent.Client.Extensions;

/// <summary>Registers the parts that decide which server an agent call reaches.</summary>
public static class AgentRoutingExtensions
{
    /// <summary>
    /// Registers the agent target, the channel pool and, unless one is already registered, the
    /// endpoint source that knows only the local server.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="socketPath">Path to the local agent's unix socket (configuration key <c>Agent:SocketPath</c>).</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The endpoint source is registered with <c>TryAdd</c> so a registry of servers that registers its
    /// own, before or after this call, is the one the pool asks: a panel that loads no such module
    /// keeps the local-only source and behaves exactly as it did before managed servers existed.
    /// </remarks>
    public static IServiceCollection AddAgentRouting(this IServiceCollection services, string socketPath)
    {
        services.AddSingleton<IAgentTarget, AsyncLocalAgentTarget>();
        services.TryAddSingleton<IAgentEndpointSource>(_ =>
        {
            return new LocalOnlyEndpointSource(socketPath);
        });
        services.AddSingleton<AgentChannelPool>();
        services.AddSingleton<IAgentChannels>(provider =>
        {
            return provider.GetRequiredService<AgentChannelPool>();
        });

        return services;
    }
}
