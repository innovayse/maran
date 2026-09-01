using Maran.Host.Configuration;
using Maran.Host.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// Builds a registry holding the real <see cref="AgentOperationPipeline"/> with a timeout short
/// enough to assert against.
/// </summary>
/// <remarks>
/// The pipeline is configured through <see cref="AgentOperationPipeline.Configure"/>, not hand-rolled
/// in the test: a decorator that is wired to a pipeline the production code never builds proves
/// nothing about production.
/// </remarks>
internal static class OperationPipelineRegistry
{
    /// <summary>Creates a registry whose agent-operation pipeline times out after <paramref name="seconds"/>.</summary>
    /// <param name="seconds">How long one attempt may take; the options type is configured in seconds.</param>
    /// <returns>The registry the decorators resolve their pipeline from.</returns>
    public static ResiliencePipelineProvider<string> WithOperationTimeout(int seconds)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline(AgentOperationPipeline.Name, builder =>
        {
            AgentOperationPipeline.Configure(builder, new AgentOptions { OperationTimeoutSeconds = seconds });
        });

        return services.BuildServiceProvider().GetRequiredService<ResiliencePipelineProvider<string>>();
    }
}
