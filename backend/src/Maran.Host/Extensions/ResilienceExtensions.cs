using Maran.Host.Configuration;
using Maran.Host.Resilience;
using Microsoft.Extensions.Options;
using Polly;

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

        return services;
    }
}
