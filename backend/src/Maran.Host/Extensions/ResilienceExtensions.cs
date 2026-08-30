using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Host.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the named resilience pipelines. Every call that leaves this process goes through one,
/// so nothing can hang forever or fail without a policy (rules/csharp.md).
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>Adds the agent-call pipeline (timeout plus bounded retry on transient failures).</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline(AgentCallPipeline.Name, (builder, context) =>
        {
            var agentOptions = context.ServiceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;
            AgentCallPipeline.Configure(builder, agentOptions);
        });

        services.AddResiliencePipeline(AgentOperationPipeline.Name, (builder, context) =>
        {
            var agentOptions = context.ServiceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;
            AgentOperationPipeline.Configure(builder, agentOptions);
        });

        DecorateAgentAccountsClient(services);

        return services;
    }

    /// <summary>
    /// Replaces the registered <see cref="IAgentAccountsClient"/> with one wrapped in
    /// <see cref="AgentOperationPipeline"/>. Must run after <c>AddAgentClient</c>, which is where
    /// the inner registration comes from; <c>Program</c> calls them in that order.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <remarks>
    /// Written by hand rather than with a decoration package: one decorator does not earn a
    /// dependency. The inner descriptor's own factory is kept and invoked, so the transport is
    /// still constructed by the project that owns the channel.
    /// </remarks>
    private static void DecorateAgentAccountsClient(IServiceCollection services)
    {
        var inner = services.Single(descriptor =>
        {
            return descriptor.ServiceType == typeof(IAgentAccountsClient);
        });

        services.Remove(inner);
        services.AddSingleton<IAgentAccountsClient>(provider =>
        {
            return new ResilientAgentAccountsClient(
                (IAgentAccountsClient)inner.ImplementationFactory!(provider),
                provider.GetRequiredService<ResiliencePipelineProvider<string>>());
        });
    }
}
