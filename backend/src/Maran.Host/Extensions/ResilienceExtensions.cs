using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Host.Resilience;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.ServerMigration.Services;
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
    /// <summary>
    /// Adds the named pipelines: the agent-call and agent-operation pipelines (timeout plus bounded retry
    /// on transient failures), the cron-run pipeline, and the timeouts of the ACME, marketplace and licence lease clients.
    /// It decorates no client; that is <see cref="AddResilientAgentClients"/>, kept apart so a host that
    /// composes a different agent registers the pipelines without there being a client of the real agent
    /// to wrap.
    /// </summary>
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

        services.AddResiliencePipeline(AgentCronRunPipeline.Name, (builder, _) =>
        {
            AgentCronRunPipeline.Configure(builder);
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

        // The same for the marketplace host: the timeout is attached to the module's named client, so a
        // call through it is governed whether or not the module remembers anything about deadlines.
        // The options are read when the client's handler chain is first built, which the transport
        // reaches only after it has validated them and the administrator has switched the marketplace on.
        services.AddHttpClient(MarketplaceOptions.HttpClientName)
            .AddResilienceHandler(MarketplacePipeline.Name, (builder, context) =>
            {
                var marketplaceOptions =
                    context.ServiceProvider.GetRequiredService<IOptions<MarketplaceOptions>>().Value;
                MarketplacePipeline.Configure(
                    builder, TimeSpan.FromSeconds(marketplaceOptions.RequestTimeoutSeconds));
            });

        // The licence lease renewal: the deadline is attached to the module's named client for the same reason.
        // The options are read when the client's handler chain is first built, which the licence service client
        // reaches only after the address passed its check.
        services.AddHttpClient(LeaseServiceOptions.HttpClientName)
            .AddResilienceHandler(OutboundHttpPipeline.Name, (builder, context) =>
            {
                var leaseOptions = context.ServiceProvider.GetRequiredService<IOptions<LeaseServiceOptions>>().Value;
                OutboundHttpPipeline.Configure(builder, TimeSpan.FromSeconds(leaseOptions.LeaseRequestTimeoutSeconds));
            });

        // And the pull of an account package from another panel (issue #127): a timeout, never a retry, because
        // the request spends a single-use grant.
        services.AddHttpClient(MigrationPullClient.HttpClientName)
            .AddResilienceHandler(MigrationPullPipeline.Name, (builder, _) =>
            {
                MigrationPullPipeline.Configure(builder);
            });

        // The verification of a migrated site (issue #197): the panel's own web server on the loopback address, with the
        // migrated domain in the Host header. The address is the module's constant, a redirect is an answer and is not
        // followed, and the timeout is the pipeline's.
        services.AddHttpClient(LoopbackSiteProbe.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(LoopbackSiteProbe.Origin);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new SocketsHttpHandler { AllowAutoRedirect = false };
            })
            .AddResilienceHandler(OutboundHttpPipeline.Name, (builder, _) =>
            {
                OutboundHttpPipeline.Configure(builder, TimeSpan.FromSeconds(15));
            });

        return services;
    }

    /// <summary>
    /// Wraps every agent client in <see cref="AgentOperationPipeline"/>. Must run after
    /// <c>AddAgentClient</c> and <see cref="AddPanelResilience"/>; <c>Program</c> calls them in that order.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddResilientAgentClients(this IServiceCollection services)
    {
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
        Decorate<IAgentFtpsClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentFtpsClient(inner, pipelines);
        });
        Decorate<IAgentDnsClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentDnsClient(inner, pipelines);
        });
        Decorate<IAgentMailClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentMailClient(inner, pipelines);
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
        Decorate<IAgentBackupClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentBackupClient(inner, pipelines);
        });
        Decorate<IAgentTransferClient>(services, (inner, pipelines) =>
        {
            return new ResilientAgentTransferClient(inner, pipelines);
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
