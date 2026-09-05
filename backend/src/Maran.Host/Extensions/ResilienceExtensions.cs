using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Host.Resilience;
using Maran.Modules.Ssl.Options;
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

        // The outbound pipeline for certificate authorities, attached to the named ACME HttpClient
        // rather than resolved by hand. A named client that carries its own pipeline cannot be
        // obtained ungoverned: the module asks the factory for "acme" and gets the policy with it.
        services.AddHttpClient(AcmeOptions.HttpClientName)
            .AddResilienceHandler(AcmePipeline.Name, (builder, context) =>
            {
                var acmeOptions = context.ServiceProvider.GetRequiredService<IOptions<AcmeOptions>>().Value;
                AcmePipeline.Configure(builder, acmeOptions.RequestTimeout);
            });

        // Every agent client is decorated here, and each one is listed once. A client registered
        // without its decorator has no timeout at all — the defect this repository already found
        // when the pipeline was registered and resolved by nobody — so the calls sit together
        // where the next client's missing line is visible.
        Decorate<IAgentAccountsClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentAccountsClient(inner, pipelines);
        });
        Decorate<IAgentSitesClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentSitesClient(inner, pipelines);
        });
        Decorate<IAgentSslClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentSslClient(inner, pipelines);
        });
        Decorate<IAgentPhpClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentPhpClient(inner, pipelines);
        });
        Decorate<IAgentFilesClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentFilesClient(inner, pipelines);
        });
        Decorate<IAgentDbClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentDbClient(inner, pipelines);
        });
        Decorate<IAgentSftpClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentSftpClient(inner, pipelines);
        });
        Decorate<IAgentCronClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentCronClient(inner, pipelines);
        });
        Decorate<IAgentFirewallClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentFirewallClient(inner, pipelines);
        });
        Decorate<IAgentMonitorClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentMonitorClient(inner, pipelines);
        });

        return services;
    }

    /// <summary>
    /// Replaces the registered <typeparamref name="TClient"/> with one wrapped in
    /// <see cref="AgentOperationPipeline"/>. Must run after <c>AddAgentClient</c>, which is where
    /// the inner registration comes from; <c>Program</c> calls them in that order.
    /// </summary>
    /// <typeparam name="TClient">The agent client contract being decorated.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="wrap">Builds the decorator around the inner client and the pipeline registry.</param>
    /// <remarks>
    /// Written by hand rather than with a decoration package: five decorators of one shape do not
    /// earn a dependency. The inner descriptor's own factory is kept and invoked, so the transport
    /// is still constructed by the project that owns the channel.
    /// </remarks>
    private static void Decorate<TClient>(
        IServiceCollection services,
        Func<TClient, ResiliencePipelineProvider<string>, TClient> wrap)
        where TClient : class
    {
        var inner = services.Single(descriptor =>
        {
            return descriptor.ServiceType == typeof(TClient);
        });

        services.Remove(inner);
        services.AddSingleton(provider =>
        {
            return wrap(
                (TClient)inner.ImplementationFactory!(provider),
                provider.GetRequiredService<ResiliencePipelineProvider<string>>());
        });
    }
}
