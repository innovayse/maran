using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Extensions;

/// <summary>Registers the licence lease renewal's outbound half.</summary>
public static class LeaseServiceClientExtensions
{
    /// <summary>Registers the licence service client and the named HTTP client it sends through.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <b>Registering the named client is not contacting anyone.</b> Nothing here starts work: the Host
    /// registers the worker that asks, on its schedule. The client is created only when
    /// <see cref="LeaseServiceClient"/> asks the factory for it, and it asks only after the options name a usable
    /// https origin. The Host attaches the outbound pipeline (the request deadline) to the same client name
    /// (<c>OutboundHttpPipeline</c>), which a module cannot reference, so the call is governed whether or not
    /// the module remembers anything about deadlines.
    /// </remarks>
    public static IServiceCollection AddLeaseServiceClient(this IServiceCollection services)
    {
        services.AddHttpClient(LeaseServiceOptions.HttpClientName, LeaseServiceClient.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(LeaseServiceClient.CreateHandler);

        services.AddSingleton<ILeaseServiceClient, LeaseServiceClient>();

        return services;
    }
}
