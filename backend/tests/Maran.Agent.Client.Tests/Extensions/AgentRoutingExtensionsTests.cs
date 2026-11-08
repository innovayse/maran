using Maran.Agent.Client.Extensions;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Models;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Agent.Client.Tests.Extensions;

/// <summary>What <c>AddAgentRouting</c> registers and which endpoint source wins.</summary>
public sealed class AgentRoutingExtensionsTests
{
    /// <summary>A panel with no registry of servers resolves the local server to its unix socket and refuses the rest.</summary>
    [Fact]
    public void Without_a_registry_only_the_local_server_resolves()
    {
        using var provider = Build(registry: null);

        var source = provider.GetRequiredService<IAgentEndpointSource>();

        Assert.Equal(new UnixSocketAgentEndpoint("/run/maran/agent.sock"), source.Resolve(FleetServers.LocalId).Value);
        Assert.False(source.Resolve(Guid.NewGuid()).IsSuccess);
    }

    /// <summary>A registry registered before the call is the source the pool asks, not the local-only one.</summary>
    [Fact]
    public void A_registry_registered_first_replaces_the_local_only_source()
    {
        var registry = new ScriptedEndpointSource();
        using var provider = Build(registry);

        Assert.Same(registry, provider.GetRequiredService<IAgentEndpointSource>());
    }

    /// <summary>The target and the channels are singletons, so every client shares one scope and one pool.</summary>
    [Fact]
    public void The_target_and_the_channels_are_singletons()
    {
        using var provider = Build(registry: null);

        Assert.Same(provider.GetRequiredService<IAgentTarget>(), provider.GetRequiredService<IAgentTarget>());
        Assert.Same(provider.GetRequiredService<IAgentChannels>(), provider.GetRequiredService<IAgentChannels>());
    }

    /// <summary>Builds a provider with the routing registered, optionally after a registry of servers.</summary>
    /// <param name="registry">The registry's endpoint source, or <c>null</c> for a panel without one.</param>
    /// <returns>The provider.</returns>
    private static ServiceProvider Build(ScriptedEndpointSource? registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(TestCertificates.Now));
        if (registry is not null)
        {
            services.AddSingleton<IAgentEndpointSource>(registry);
        }

        services.AddAgentRouting("/run/maran/agent.sock");
        return services.BuildServiceProvider();
    }
}
